using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 仅在当前测试临时文件系统真实支持文件符号链接时运行。
/// E 盘可能使用不支持重解析点的文件系统；这种情况下由测试框架明确记为跳过，
/// 其余路径穿越、祖先目录和普通文件校验仍由不依赖符号链接的测试持续覆盖。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SymbolicLinkFactAttribute : FactAttribute
{
    private static readonly Lazy<(bool Supported, string Reason)> Capability = new(Probe);

    public SymbolicLinkFactAttribute()
    {
        var capability = Capability.Value;
        if (!capability.Supported)
            Skip = $"当前临时文件系统不支持符号链接安全用例：{capability.Reason}";
    }

    private static (bool Supported, string Reason) Probe()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "GrandUMI-SymbolicLinkCapability",
            Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target.txt");
        var link = Path.Combine(root, "link.txt");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(target, "probe");
            File.CreateSymbolicLink(link, target);
            var info = new FileInfo(link);
            bool isLink = info.Exists
                && (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0);
            return isLink
                ? (true, string.Empty)
                : (false, "创建操作未产生可识别的符号链接");
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return (false, $"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(link) || new FileInfo(link).LinkTarget is not null) File.Delete(link);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 探测清理失败不应掩盖真实的能力结论；测试总临时目录会在统一验证后清理。
            }
        }
    }
}
