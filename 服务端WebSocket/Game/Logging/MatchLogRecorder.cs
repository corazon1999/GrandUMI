using System.Text.Json;

namespace GrandUMI.Game.Logging;

public static class MatchLogRecorder
{
    private static readonly Dictionary<string, StreamWriter> Writers = new();
    private static readonly Dictionary<string, long> Sequences = new();
    private static readonly object LockObj = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
        lock (LockObj)
        {
            var root = GetLogDir();
            var dayDir = Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayDir);
            var path = Path.Combine(dayDir, $"{matchId}.jsonl");
            var writer = new StreamWriter(path, append: false) { AutoFlush = true };
            Writers[matchId] = writer;
            Sequences[matchId] = 0;
            return path;
        }
    }

    /// <summary>重启恢复后以追加模式重开已有日志（跨天会落到原文件；找不到则新建当天文件）。seq 接着已有行数递增。</summary>
    public static string OpenAppend(string matchId)
    {
        lock (LockObj)
        {
            var root = GetLogDir();
            string? existing = null;
            if (Directory.Exists(root))
            {
                var hits = Directory.GetFiles(root, $"{matchId}.jsonl", SearchOption.AllDirectories);
                if (hits.Length > 0) existing = hits[0];
            }
            var path = existing ?? Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{matchId}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            long startSeq = 0;
            if (existing is not null)
            {
                try { startSeq = File.ReadLines(existing).LongCount(); } catch { }
            }
            var writer = new StreamWriter(path, append: true) { AutoFlush = true };
            Writers[matchId] = writer;
            Sequences[matchId] = startSeq;
            return path;
        }
    }

    public static void Append(string matchId, GameState state, string kind, int? actor, object? payload)
    {
        lock (LockObj)
        {
            if (!Writers.TryGetValue(matchId, out var writer)) return;

            try
            {
                var seq = Sequences.TryGetValue(matchId, out var current) ? current + 1 : 1;
                Sequences[matchId] = seq;

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

                writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
            }
            catch
            {
                // Match log must never interrupt the game flow.
            }
        }
    }

    public static void Close(string matchId)
    {
        lock (LockObj)
        {
            if (Writers.TryGetValue(matchId, out var writer))
            {
                try { writer.Flush(); writer.Dispose(); } catch { }
                Writers.Remove(matchId);
            }
            Sequences.Remove(matchId);
        }
    }
}
