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
    public void Windows测试临时目录固定在GrandUMI的E盘根下()
    {
        if (!OperatingSystem.IsWindows()) return;

        var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var expectedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(@"E:\GrandUMI-Temp")) + Path.DirectorySeparatorChar;

        Assert.StartsWith(expectedRoot, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Windows测试进程复用部署门禁提供的唯一临时根()
    {
        if (!OperatingSystem.IsWindows()) return;

        var configured = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(configured));

        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured!));
        var actualTemp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Environment.GetEnvironmentVariable("TEMP") ?? ""));
        var actualTmp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Environment.GetEnvironmentVariable("TMP") ?? ""));
        var actualFrameworkTemp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));

        Assert.Equal(expected, actualTemp, ignoreCase: true);
        Assert.Equal(expected, actualTmp, ignoreCase: true);
        Assert.Equal(expected, actualFrameworkTemp, ignoreCase: true);
    }

    [Fact]
    public void Windows测试临时目录解析器原样复用门禁隔离根()
    {
        if (!OperatingSystem.IsWindows()) return;

        var configured = @"E:\GrandUMI-Temp\Verify\run-0123456789abcdef";

        var actual = GrandUmiTestTempPolicy.ResolveWindowsTempRoot(
            configured,
            processId: 123,
            fallbackToken: "unused");

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured)),
            actual,
            ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\Temp\grandumi")]
    [InlineData(@"E:\GrandUMI-Temp-Other\run")]
    [InlineData(@"E:\GrandUMI-Temp")]
    [InlineData("relative-temp")]
    [InlineData("")]
    [InlineData("   ")]
    public void Windows测试临时目录解析器拒绝越界或不明确的门禁根(string configured)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Throws<InvalidOperationException>(() =>
            GrandUmiTestTempPolicy.ResolveWindowsTempRoot(
                configured,
                processId: 123,
                fallbackToken: "unused"));
    }

    [Fact]
    public void Windows直接运行测试时为每个测试主机生成唯一E盘回退根()
    {
        if (!OperatingSystem.IsWindows()) return;

        var first = GrandUmiTestTempPolicy.ResolveWindowsTempRoot(
            configuredRoot: null,
            processId: 123,
            fallbackToken: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var second = GrandUmiTestTempPolicy.ResolveWindowsTempRoot(
            configuredRoot: null,
            processId: 123,
            fallbackToken: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.Equal(
            Path.GetFullPath(
                @"E:\GrandUMI-Temp\Tests\testhost-123-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            first,
            ignoreCase: true);
        Assert.False(string.Equals(first, second, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, "aaaaaaaa")]
    [InlineData(-1, "aaaaaaaa")]
    [InlineData(123, "")]
    [InlineData(123, "../escape")]
    public void Windows直接运行测试时拒绝无效的测试主机身份(
        int processId,
        string fallbackToken)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Throws<InvalidOperationException>(() =>
            GrandUmiTestTempPolicy.ResolveWindowsTempRoot(
                configuredRoot: null,
                processId,
                fallbackToken));
    }
}
