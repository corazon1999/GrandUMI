using GrandUMI.Diagnostics;
using Xunit;

namespace GrandUMI.Tests;

public sealed class GrandUmiTestTempPolicyTests
{
    [Fact]
    public void 未提供测试覆盖时仍读取生产内存压力来源()
    {
        var productionCalls = 0;
        var expected = new ServerCapacity.MemoryPressureSnapshot(90, 100);

        var actual = ServerCapacity.ResolveMemoryPressureSnapshotForTesting(
            () =>
            {
                productionCalls++;
                return expected;
            },
            testProvider: null);

        Assert.Equal(expected, actual);
        Assert.Equal(1, productionCalls);
    }

    [Fact]
    public void 提供测试覆盖时不读取生产内存压力来源()
    {
        var expected = new ServerCapacity.MemoryPressureSnapshot(0, 1);

        var actual = ServerCapacity.ResolveMemoryPressureSnapshotForTesting(
            () => throw new InvalidOperationException("测试覆盖存在时不应读取宿主 GC 压力。"),
            () => expected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 测试程序集只安装一次固定无压力覆盖()
    {
        Assert.True(ServerCapacity.HasMemoryPressureProviderForTesting);
        Assert.Equal(
            new ServerCapacity.MemoryPressureSnapshot(0, 1),
            ServerCapacity.ReadEffectiveMemoryPressureForTesting());

        Assert.Throws<InvalidOperationException>(() =>
            ServerCapacity.SetMemoryPressureProviderForTesting(
                static () => new ServerCapacity.MemoryPressureSnapshot(1, 1)));
    }

    [Fact]
    public void Windows测试临时目录固定在E盘()
    {
        if (!OperatingSystem.IsWindows()) return;

        var actual = Path.GetFullPath(Path.GetTempPath());
        var expectedRoot = Path.GetFullPath(@"E:\GrandUMI-Temp\Tests");

        Assert.StartsWith(expectedRoot, actual, StringComparison.OrdinalIgnoreCase);
    }
}
