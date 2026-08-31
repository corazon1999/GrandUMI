using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrandUMI;

/// <summary>
/// 游戏反馈兼容落盘：按反馈 ID 使用稳定路径，重复请求、进程重启和并发写入都只产生一个文件。
/// 文件只含白名单化客户端诊断与服务端权威摘要，不得写入全量牌局快照。
/// </summary>
public static class BugReportStore
{
    internal const int MaxReportBytes = 64 * 1024;
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义
    };

    private static string? _root;
    private static readonly object _lock = new();
    private static readonly object _writeLock = new();

    /// <summary>原子保存一条反馈；相同 feedbackId 幂等返回既有路径。</summary>
    public static string Save(object report, string feedbackId, string category)
        => SaveAtRoot(report, GetRoot(), feedbackId, category);

    internal static string SaveAtRoot(object report, string root, string feedbackId, string category)
    {
        var serialized = JsonSerializer.Serialize(report, WriteOpts);
        if (Encoding.UTF8.GetByteCount(serialized) > MaxReportBytes)
            throw new InvalidDataException($"反馈证据超过 {MaxReportBytes} 字节上限");

        var fullPath = BuildReportPath(root, feedbackId);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("反馈目录无效");

        lock (_writeLock)
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(fullPath)) return fullPath;

            var temporaryPath = fullPath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(serialized);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                try { File.Move(temporaryPath, fullPath, overwrite: false); }
                catch (IOException) when (File.Exists(fullPath)) { }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            return fullPath;
        }
    }

    internal static string BuildReportPath(string root, string feedbackId)
    {
        var normalizedFeedbackId = string.IsNullOrWhiteSpace(feedbackId) ? "invalid" : feedbackId.Trim();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"grandumi.bug-report.path.v1\0{normalizedFeedbackId}")))
            .ToLowerInvariant();
        return Path.Combine(Path.GetFullPath(root), "by-id", digest[..2], $"{digest}.json");
    }

    private static string GetRoot()
    {
        if (_root is not null) return _root;
        lock (_lock)
        {
            if (_root is not null) return _root;
            // 向上查找含"卡牌数据"的目录作为项目根，把 BugReports 放在同级
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? projectRoot = null;
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "卡牌数据"))) { projectRoot = dir.FullName; break; }
                dir = dir.Parent;
            }
            _root = ResolveRoot(
                Environment.GetEnvironmentVariable("GRANDUMI_BUG_REPORT_DIR"),
                Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR"),
                projectRoot ?? AppContext.BaseDirectory);
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    internal static string ResolveRoot(string? explicitRoot, string? dataRoot, string fallbackRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);
        if (!string.IsNullOrWhiteSpace(dataRoot)) return Path.Combine(Path.GetFullPath(dataRoot), "BugReports");
        return Path.Combine(Path.GetFullPath(fallbackRoot), "BugReports");
    }

}
