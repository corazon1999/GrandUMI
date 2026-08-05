using System.Text.Json;

namespace GrandUMI.Game.Logging;

public static class MatchLogRecorder
{
    private static readonly Dictionary<string, long> Sequences = new();
    private static readonly object LockObj = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly AsyncJsonlWriter Writer = new(JsonOptions);

    public static string GetLogDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "MatchLogs");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "MatchLogs");
    }

    public static string Open(string matchId)
    {
        var path = Path.Combine(GetLogDir(), DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{matchId}.jsonl");
        lock (LockObj)
            Sequences[matchId] = 0;
        Writer.Open(matchId, path, append: false);
        return path;
    }

    /// <summary>重启恢复后以追加模式重开已有日志，序号接着已有行数递增。</summary>
    public static string OpenAppend(string matchId)
    {
        var root = GetLogDir();
        string? existing = null;
        if (Directory.Exists(root))
        {
            var hits = Directory.GetFiles(root, $"{matchId}.jsonl", SearchOption.AllDirectories);
            if (hits.Length > 0) existing = hits[0];
        }

        var path = existing ?? Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{matchId}.jsonl");
        long startSeq = 0;
        if (existing is not null)
        {
            try { startSeq = File.ReadLines(existing).LongCount(); } catch { }
        }

        lock (LockObj)
            Sequences[matchId] = startSeq;
        Writer.Open(matchId, path, append: true);
        return path;
    }

    public static void Append(string matchId, GameState state, string kind, int? actor, object? payload)
    {
        object entry;
        lock (LockObj)
        {
            if (!Sequences.TryGetValue(matchId, out var current)) return;
            var seq = current + 1;
            Sequences[matchId] = seq;

            // 在游戏线程只捕获标量与不可变快照；JSON 序列化和文件 I/O 留给后台线程。
            entry = new
            {
                schema = "grandumi.matchlog.v1",
                matchId,
                seq,
                tick = state.Tick,
                turn = state.TurnCount,
                phase = PhaseLabels.Of(state.Phase),
                timeUtc = DateTime.UtcNow,
                kind,
                actor,
                payload = payload ?? new { },
            };
        }

        Writer.Append(matchId, entry);
    }

    /// <summary>关闭前会等待该房间已经入队的日志全部落盘。</summary>
    public static void Close(string matchId)
    {
        Writer.Close(matchId);
        lock (LockObj)
            Sequences.Remove(matchId);
    }

    /// <summary>正常关服时排空队列并关闭全部日志文件。</summary>
    public static void Shutdown() => Writer.Shutdown();
}
