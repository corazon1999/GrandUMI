using Xunit;

namespace GrandUMI.Tests;

public sealed class GrandUmiTestTempPolicyTests
{
    [Fact]
    public void Windows测试临时目录固定在E盘()
    {
        if (!OperatingSystem.IsWindows()) return;

        var actual = Path.GetFullPath(Path.GetTempPath());
        var expectedRoot = Path.GetFullPath(@"E:\GrandUMI-Temp\Tests");

        Assert.StartsWith(expectedRoot, actual, StringComparison.OrdinalIgnoreCase);
    }
}
