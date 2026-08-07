using GrandUMI;
using GrandUMI.Cards;
using GrandUMI.Game.Stats;
using GrandUMI.Persistence;
using System.Runtime.Loader;

Console.Title = "GrandUMI WebSocket 服务器";
Console.OutputEncoding = System.Text.Encoding.UTF8;

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 8080;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║    GrandUMI WebSocket 服务器          ║");
Console.WriteLine($"║    ws://localhost:{port}/ws/              ║");
Console.WriteLine("║    按 Ctrl+C 停止                     ║");
Console.WriteLine("╚══════════════════════════════════════╝\n");

// 玩家数据存放在 publish 目录之外，避免发布替换程序时丢失。
var playerDataStore = new PlayerDataStore(PlayerDataStore.ResolveDefaultPath());
playerDataStore.Initialize();
Console.WriteLine($"[玩家数据] SQLite: {playerDataStore.DatabasePath}");

// 加载卡牌数据库（项目根目录下的"卡牌数据"目录）
var cardDataPath = ResolveCardDataPath();
CardDatabase.LoadFrom(cardDataPath);

// 加载效果 DSL 定义（整个 Definitions 目录下所有 *.json）
var dslDir = ResolveDslDir();
GrandUMI.Effects.Dsl.DslInterpreter.LoadDirectory(dslDir);

// Leader 排行榜使用独立 SQLite；线上可用 GRANDUMI_DATA_DIR 指向持久化目录。
LeaderStatsStore.Default.Initialize();
Console.WriteLine($"[LeaderStats] SQLite: {LeaderStatsStore.Default.DatabasePath}");

// 重启恢复：把 TTL 内未结束的 PvP 对局重放重建回内存（须在卡库/DSL 加载之后、开监听之前）
await GrandUMI.Game.GameRoomManager.RestoreAll();

WebSocketBridge.Start(port, playerDataStore);

// 等待 Ctrl+C
var tcs = new TaskCompletionSource();
var stopping = 0;
void RequestStop()
{
    if (Interlocked.Exchange(ref stopping, 1) != 0) return;
    Console.WriteLine("\n[服务器] 正在停止...");
    WebSocketBridge.Stop();
    tcs.TrySetResult();
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    RequestStop();
};
// Linux/systemd 的 SIGTERM 会触发卸载回调，确保发布重启时也能排空日志队列。
AssemblyLoadContext.Default.Unloading += _ => RequestStop();

await tcs.Task;
// 先停止接收新消息，再排空诊断日志与录像队列，避免正常关服丢失队尾数据。
GrandUMI.Game.ReplayRecorder.Shutdown();
GrandUMI.Game.Logging.MatchLogRecorder.Shutdown();
Console.WriteLine("[服务器] 已停止");
return;

// ─── 卡牌数据路径解析（向上查找直到找到"卡牌数据"目录） ───
static string ResolveCardDataPath()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "卡牌数据");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "卡牌数据"));
}

static string ResolveDslDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "Effects", "Definitions");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Effects", "Definitions"));
}
