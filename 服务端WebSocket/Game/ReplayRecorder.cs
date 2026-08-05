using GrandUMI.Game.Logging;

namespace GrandUMI.Game;

/// <summary>
/// 把每局对战的 GameAction / GameState 序列以 jsonl 写盘。
/// 写入由后台单线程批处理，游戏结算线程不直接等待磁盘 I/O。
/// </summary>
public static class ReplayRecorder
{
    private static readonly AsyncJsonlWriter Writer = new();

    public static string GetReplayDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Replays");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Replays");
    }

    public static string Open(string roomId)
    {
        var path = Path.Combine(GetReplayDir(), DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{roomId}.jsonl");
        Writer.Open(roomId, path, append: false);
        return path;
    }

    /// <summary>重启恢复后以追加模式重开已有录像。</summary>
    public static string OpenAppend(string roomId)
    {
        var root = GetReplayDir();
        string? existing = null;
        if (Directory.Exists(root))
        {
            var hits = Directory.GetFiles(root, $"{roomId}.jsonl", SearchOption.AllDirectories);
            if (hits.Length > 0) existing = hits[0];
        }

        var path = existing ?? Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{roomId}.jsonl");
        Writer.Open(roomId, path, append: true);
        return path;
    }

    public static void Append(string roomId, object entry)
        => Writer.Append(roomId, entry);

    /// <summary>关闭前会等待该房间已经入队的录像全部落盘。</summary>
    public static void Close(string roomId)
        => Writer.Close(roomId);

    /// <summary>正常关服时排空队列并关闭全部录像文件。</summary>
    public static void Shutdown() => Writer.Shutdown();
}
