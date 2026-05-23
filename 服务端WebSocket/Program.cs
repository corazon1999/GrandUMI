using GrandUMI;
using GrandUMI.Cards;

Console.Title = "GrandUMI WebSocket 服务器";
Console.OutputEncoding = System.Text.Encoding.UTF8;

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 8080;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║    GrandUMI WebSocket 服务器          ║");
Console.WriteLine($"║    ws://localhost:{port}/ws/              ║");
Console.WriteLine("║    按 Ctrl+C 停止                     ║");
Console.WriteLine("╚══════════════════════════════════════╝\n");

// 加载卡牌数据库（项目根目录下的"卡牌数据"目录）
var cardDataPath = ResolveCardDataPath();
CardDatabase.LoadFrom(cardDataPath);

// 加载效果 DSL 定义
var dslPath = ResolveDslPath();
GrandUMI.Effects.Dsl.DslInterpreter.Load(dslPath);

WebSocketBridge.Start(port);

// 等待 Ctrl+C
var tcs = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[服务器] 正在停止...");
    WebSocketBridge.Stop();
    tcs.SetResult();
};

await tcs.Task;
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

static string ResolveDslPath()
{
    // 服务端项目根目录下的 Effects/Definitions/OP15.json
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "Effects", "Definitions", "OP15.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Effects", "Definitions", "OP15.json"));
}
