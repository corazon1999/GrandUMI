using System.Text.Json;

namespace GrandUMI.Game;

/// <summary>
/// 把每局对战的 GameAction / GameState 序列以 jsonl 写盘
/// 用于观战回放（M6）和 e2e 测试
/// 文件路径：Replays/{yyyy-MM-dd}/{roomId}.jsonl
/// </summary>
public static class ReplayRecorder
{
    private static readonly Dictionary<string, StreamWriter> _writers = new();
    private static readonly object _lock = new();

    public static string GetReplayDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // 在服务端项目根目录创建 Replays
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Replays");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Replays");
    }

    public static string Open(string roomId)
    {
        lock (_lock)
        {
            var root = GetReplayDir();
            var dayDir = Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayDir);
            var path = Path.Combine(dayDir, $"{roomId}.jsonl");
            var w = new StreamWriter(path, append: false) { AutoFlush = true };
            _writers[roomId] = w;
            return path;
        }
    }

    /// <summary>重启恢复后以追加模式重开已有录像（跨天会落到原文件；找不到则新建当天文件）。</summary>
    public static string OpenAppend(string roomId)
    {
        lock (_lock)
        {
            var root = GetReplayDir();
            string? existing = null;
            if (Directory.Exists(root))
            {
                var hits = Directory.GetFiles(root, $"{roomId}.jsonl", SearchOption.AllDirectories);
                if (hits.Length > 0) existing = hits[0];
            }
            var path = existing ?? Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"), $"{roomId}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var w = new StreamWriter(path, append: true) { AutoFlush = true };
            _writers[roomId] = w;
            return path;
        }
    }

    public static void Append(string roomId, object entry)
    {
        lock (_lock)
        {
            if (!_writers.TryGetValue(roomId, out var w)) return;
            try
            {
                var line = JsonSerializer.Serialize(entry);
                w.WriteLine(line);
            }
            catch { /* 不影响主流程 */ }
        }
    }

    public static void Close(string roomId)
    {
        lock (_lock)
        {
            if (_writers.TryGetValue(roomId, out var w))
            {
                try { w.Flush(); w.Dispose(); } catch { }
                _writers.Remove(roomId);
            }
        }
    }
}
