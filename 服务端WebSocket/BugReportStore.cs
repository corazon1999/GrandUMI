using System.Text.Json;

namespace GrandUMI;

/// <summary>
/// Bug 反馈落盘：根目录 BugReports/ → 按日期(yyyy-MM-dd)分子目录 → 每条反馈一个 JSON 文件。
/// 文件内含反馈描述 + 客户端全量信息 + 服务端权威全量快照，便于定位 bug。
/// </summary>
public static class BugReportStore
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义
    };

    private static string? _root;
    private static readonly object _lock = new();

    /// <summary>保存一条反馈，返回写入文件的完整路径。</summary>
    public static string Save(object report, string account, string? roomId)
    {
        var now = DateTime.Now;
        var dateDir = Path.Combine(GetRoot(), now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dateDir);

        var safeAccount = Sanitize(string.IsNullOrEmpty(account) ? "anon" : account);
        var safeRoom = string.IsNullOrEmpty(roomId) ? "noroom" : Sanitize(roomId);
        var fileName = $"{now:HH-mm-ss}_{safeAccount}_{safeRoom}.json";
        var fullPath = Path.Combine(dateDir, fileName);

        // 同一秒多条反馈防覆盖
        int dup = 1;
        while (File.Exists(fullPath))
            fullPath = Path.Combine(dateDir, $"{now:HH-mm-ss}_{safeAccount}_{safeRoom}_{dup++}.json");

        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, WriteOpts), System.Text.Encoding.UTF8);
        return fullPath;
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
            _root = Path.Combine(projectRoot ?? AppContext.BaseDirectory, "BugReports");
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Length > 40 ? s[..40] : s;
    }
}
