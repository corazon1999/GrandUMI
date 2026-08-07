using System.Text.Json;
using GrandUMI.Game.Logging;

namespace GrandUMI.Game;

/// <summary>
/// 房间动作日志（重启恢复用）：每个 PvP 房间一份 <c>Persist/&lt;roomId&gt;.jsonl</c>，
/// 记录"重放重建所需的最小信息" —— 首行 header（seed/牌组/先手等）+ 之后每个被接受动作一行。
///
/// 设计要点：
/// - 扁平存放（不按天分目录），便于服务器启动时一次扫描 Persist/ 目录恢复未结束的对局。
/// - 分胜负/房间结束时删除该文件（<see cref="Delete"/>），避免恢复已结束的局。
/// - 仅持久化"被接受"的动作（拒绝的不写），重放时才不会引入分歧。
/// - 每条 action 带 tsUtc 时间戳，供恢复时按"自最后一次操作起 30 分钟"做 TTL 判定。
/// 写盘失败一律吞掉，绝不影响对局主流程（仿 ReplayRecorder / MatchLogRecorder）。
/// </summary>
public static class RoomJournal
{
    private static readonly AsyncJsonlWriter Writer = new(jsonOptions: null, capacity: 8_192);

    /// <summary>持久化根目录：服务端项目根下的 Persist/（向上查找 GrandUMIServer.csproj）。</summary>
    public static string GetPersistDir()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dataDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "Persist"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Persist");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Persist");
    }

    private static string PathOf(string roomId)
        => Path.Combine(GetPersistDir(), $"{roomId}.jsonl");

    /// <summary>建文件并写入 header（首行 {kind:"create", ...}）。append:false 覆盖旧残留。</summary>
    public static void Open(string roomId, object header)
    {
        try
        {
            Writer.Open(roomId, PathOf(roomId), append: false);
            Writer.Append(roomId, header);
        }
        catch { /* 不影响主流程 */ }
    }

    /// <summary>以追加模式重开已有日志（重启恢复后续写新动作，不重写 header）。</summary>
    public static void Reopen(string roomId)
    {
        try { Writer.Open(roomId, PathOf(roomId), append: true); }
        catch { /* 不影响主流程 */ }
    }

    /// <summary>追加一个被接受的动作（{kind:"action", playerIndex, action, data, tsUtc}）。</summary>
    public static void Append(string roomId, int playerIndex, string action, JsonElement data)
    {
        try
        {
            Writer.Append(roomId, new
            {
                kind = "action",
                playerIndex,
                action,
                data,
                tsUtc = DateTime.UtcNow,
            });
        }
        catch { /* 不影响主流程 */ }
    }

    /// <summary>关闭并删除该房间的日志（分胜负/结束时调用）。</summary>
    public static void Delete(string roomId)
        => DeleteDeferred(roomId).GetAwaiter().GetResult();

    public static Task DeleteDeferred(string roomId)
    {
        try { return Writer.DeleteDeferred(roomId, PathOf(roomId)); }
        catch { return Task.CompletedTask; }
    }

    public static int QueueDepth => Writer.QueueDepth;
    public static long DroppedEntries => Writer.DroppedEntries;
    public static void Shutdown() => Writer.Shutdown();
}
