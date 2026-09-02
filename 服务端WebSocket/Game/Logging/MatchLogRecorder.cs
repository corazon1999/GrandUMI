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

    /// <summary>仅供终局持久化故障演练测试注入；生产代码不得设置。</summary>
    internal static Func<string, string, Exception?>? DurableFailureInjector { get; set; }

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
        var existing = FindExistingPath(root, matchId);

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

    /// <summary>进程恢复时判断终局事实是否已经写入，避免崩溃点重放追加第二条 match_end。</summary>
    internal static bool ContainsKind(string matchId, string kind)
    {
        var existing = FindExistingPath(GetLogDir(), matchId);
        if (existing is null) return false;
        using var stream = new FileStream(
            existing,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("kind", out var storedKind)
                    && string.Equals(storedKind.GetString(), kind, StringComparison.Ordinal))
                    return true;
            }
            catch (JsonException)
            {
                // 普通对局日志不是恢复权威；损坏行由既有回放审计处理，这里只寻找已提交终局。
            }
        }
        return false;
    }

    private static string? FindExistingPath(string root, string matchId)
    {
        if (!Directory.Exists(root)) return null;
        var hits = Directory.GetFiles(root, $"{matchId}.jsonl", SearchOption.AllDirectories);
        return hits.Length > 0 ? hits[0] : null;
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

    /// <summary>
    /// 终局等权威行只有在物理刷新完成后才返回成功。序号仅在刷新成功后推进，注入或写盘失败
    /// 均可安全重试；跨进程重试再由 ContainsKind 判断是否已经提交。
    /// </summary>
    internal static MatchLogAppendReceipt AppendDurableRequired(
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
            if (DurableFailureInjector?.Invoke(matchId, kind) is { } injected)
                throw injected;
            Writer.AppendDurable(matchId, entry);
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
