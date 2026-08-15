using GrandUMI.Persistence;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class SingleWriterLeaseTests
{
    [Fact]
    public void 同一数据目录不允许两个写入进程租约()
    {
        if (OperatingSystem.IsMacOS()) return;
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        try
        {
            using var first = SingleWriterLease.Acquire(root, "node-a");
            var error = Assert.Throws<InvalidOperationException>(() =>
                SingleWriterLease.Acquire(root, "node-b"));
            Assert.Contains("拒绝双写", error.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string TestDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(@"E:\GrandUMI-Temp\Tests", $"writer-lease-{Guid.NewGuid():N}");
        return Path.Combine(Path.GetTempPath(), $"grandumi-writer-lease-{Guid.NewGuid():N}");
    }
}
