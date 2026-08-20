using GrandUMI.Diagnostics;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class StorageHealthTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsWindows()
            ? @"E:\GrandUMI-Temp\Tests"
            : "/tmp/grandumi-tests",
        $"grandumi-storage-health-{Guid.NewGuid():N}");

    public StorageHealthTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void 可写且空间充足时健康()
    {
        var snapshot = StorageHealth.Inspect(_directory, 1, 0);

        Assert.True(snapshot.Healthy);
        Assert.Equal("", snapshot.Reason);
        Assert.True(snapshot.TotalBytes > 0);
        Assert.True(snapshot.AvailableBytes > 0);
        Assert.Empty(Directory.GetFiles(_directory, ".storage-health-*.tmp"));
    }

    [Fact]
    public void 可用空间低于阈值时报压力()
    {
        var snapshot = StorageHealth.Inspect(_directory, long.MaxValue, 0);

        Assert.False(snapshot.Healthy);
        Assert.Equal("storage_pressure", snapshot.Reason);
    }

    [Fact]
    public void 数据目录不存在时报不可用()
    {
        var missing = Path.Combine(_directory, "missing");

        var snapshot = StorageHealth.Inspect(missing, 1, 0);

        Assert.False(snapshot.Healthy);
        Assert.Equal("storage_unavailable", snapshot.Reason);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
