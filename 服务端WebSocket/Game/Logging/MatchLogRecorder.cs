using System.Text.Json;

namespace GrandUMI.Game.Logging;

/// <summary>日志事件获得的权威序号及其是否成功进入有序写入队列。</summary>
public readonly record struct MatchLogAppendReceipt(long Seq, bool Queued);

public static class MatchLogRecorder
{
    private static readonly Dictionary<string, long> Sequences = new();
    private static readonly object LockObj = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly AsyncJsonlWriter Writer = new(JsonOptions);

    public static int QueueDepth => Writer.QueueDepth;
    public static long DroppedEntries => Writer.DroppedEntries;

    public static string GetLogDir()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dataDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "MatchLogs"));

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
        return OpenAt(matchId, path);
    }

    internal static string OpenAt(string matchId, string path)
    {
        lock (LockObj)
        {
            Writer.Open(matchId, path, append: false);
            Sequences[matchId] = 0;
        }
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
        {
            Writer.Open(matchId, path, append: true);
            Sequences[matchId] = startSeq;
        }
        return path;
    }

    public static MatchLogAppendReceipt Append(
        string matchId,
        GameState state,
        string kind,
        int? actor,
        object? payload)
    {
        lock (LockObj)
        {
            if (!Sequences.TryGetValue(matchId, out var current))
                return new MatchLogAppendReceipt(0, false);
            var seq = current + 1;
            Sequences[matchId] = seq;

            // 在游戏线程只捕获标量与不可变快照；JSON 序列化和文件 I/O 留给后台线程。
            var entry = new
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

            // 序号分配与入队必须处于同一临界区；否则并发的开局续延可能让 seq=2
            // 比 seq=1 更早进入 Channel，破坏 append-only 日志的物理顺序。
            var queued = Writer.Append(matchId, entry);
            return new MatchLogAppendReceipt(seq, queued);
        }
    }

    /// <summary>checkpoint 等关键行在容量饱和时等待入队，并保持与普通事件同一序号/Channel 顺序。</summary>
    public static MatchLogAppendReceipt AppendRequired(
        string matchId,
        GameState state,
        string kind,
        int? actor,
        object? payload)
    {
        lock (LockObj)
        {
            if (!Sequences.TryGetValue(matchId, out var current))
                return new MatchLogAppendReceipt(0, false);
            var seq = current + 1;
            var entry = new
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
            Writer.AppendRequired(matchId, entry);
            Sequences[matchId] = seq;
            return new MatchLogAppendReceipt(seq, true);
        }
    }

    /// <summary>关闭前会等待该房间已经入队的日志全部落盘。</summary>
    public static void Close(string matchId)
    {
        Task completion;
        lock (LockObj)
        {
            completion = Writer.CloseDeferred(matchId);
            Sequences.Remove(matchId);
        }
        completion.GetAwaiter().GetResult();
    }

    public static Task CloseDeferred(string matchId)
    {
        lock (LockObj)
        {
            var completion = Writer.CloseDeferred(matchId);
            Sequences.Remove(matchId);
            return completion;
        }
    }

    /// <summary>正常关服时排空队列并关闭全部日志文件。</summary>
    public static void Shutdown() => Writer.Shutdown();
}
