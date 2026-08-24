using GrandUMI;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class AdminDeploymentCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsWindows() ? @"E:\GrandUMI-Temp\Tests" : "/tmp/grandumi-tests",
        $"grandumi-admin-deploy-{Guid.NewGuid():N}");

    [Fact]
    public void 发布请求只写入白名单环境和随机请求文件()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        Directory.CreateDirectory(Path.Combine(_directory, "status"));
        var coordinator = new AdminDeploymentCoordinator(_directory);
        coordinator.Initialize();

        var status = coordinator.Queue("test");

        Assert.Equal("queued", status.State);
        var request = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "requests"), "test-*.request"));
        var content = File.ReadAllText(request);
        Assert.Contains("environment=test", content);
        Assert.Matches("nonce=[0-9a-f]{32}", content);
        Assert.Throws<ArgumentException>(() => coordinator.Queue("candidate"));
    }

    [Fact]
    public void 能读取执行器状态且不会执行状态中的文本()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        Directory.CreateDirectory(Path.Combine(_directory, "status"));
        File.WriteAllText(
            Path.Combine(_directory, "status", "production.status"),
            "state=failed\ntarget=0123456789012345678901234567890123456789\nmessage=5q2j5byP5pS+5bu65Lq65bel6L+Q6KGM5Lit44CC\nupdated=1787625600\n");
        var coordinator = new AdminDeploymentCoordinator(_directory);
        coordinator.Initialize();

        var status = coordinator.GetStatus("production");

        Assert.Equal("failed", status.State);
        Assert.Equal("0123456789012345678901234567890123456789", status.TargetCommit);
        Assert.NotEmpty(status.Message);
        Assert.NotNull(status.UpdatedAt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
