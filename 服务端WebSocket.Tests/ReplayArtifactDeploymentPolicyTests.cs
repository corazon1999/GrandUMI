using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ReplayArtifactDeploymentPolicyTests
{
    [Fact]
    public void 测试服部署_先归档验证再切换并在失败时恢复身份环境()
    {
        var script = File.ReadAllText(RepoPath("ops", "server", "deploy-test.sh"));
        var capture = script.IndexOf("--replay-artifact capture", StringComparison.Ordinal);
        var verify = script.IndexOf("--replay-artifact verify", StringComparison.Ordinal);
        var switchPublish = script.IndexOf(
            "mv \"$next_publish\" \"$repo/服务端WebSocket/publish\"",
            StringComparison.Ordinal);
        var restart = script.IndexOf(
            "systemctl restart grandumi-test-backend.service",
            StringComparison.Ordinal);
        var audit = script.IndexOf("--replay-artifact audit", StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(verify > capture);
        Assert.True(switchPublish > verify);
        Assert.True(restart > switchPublish);
        Assert.True(audit > restart);
        Assert.Contains(
            "replay_archive_root=/var/lib/grandumi-test-replay-artifacts",
            script,
            StringComparison.Ordinal);
        Assert.Contains(".staging", script, StringComparison.Ordinal);
        Assert.Contains("replay_env_backup", script, StringComparison.Ordinal);
        Assert.Contains("mv \"$replay_env_backup\" \"$replay_archive_env\"", script, StringComparison.Ordinal);
        Assert.Contains("已尝试回滚", script, StringComparison.Ordinal);
    }

    [Fact]
    public void 测试服Service_归档只读且缺绑定时FailClosed()
    {
        var service = File.ReadAllText(RepoPath("ops", "server", "grandumi-test-backend.service"));

        Assert.Contains(
            "EnvironmentFile=-/etc/grandumi/grandumi-test-replay-artifact.env",
            service,
            StringComparison.Ordinal);
        Assert.Contains(
            "Environment=GRANDUMI_REQUIRE_REPLAY_ARTIFACT_ARCHIVE=1",
            service,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReadOnlyPaths=/data/grandumi /var/lib/grandumi-test-replay-artifacts",
            service,
            StringComparison.Ordinal);
        var readWriteLine = service.Split('\n')
            .Single(line => line.StartsWith("ReadWritePaths=", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "/var/lib/grandumi-test-replay-artifacts",
            readWriteLine,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 候选服与正式服部署_未接入测试服归档路径()
    {
        var files = new[]
        {
            RepoPath("ops", "server", "grandumi-candidate-backend.service"),
            RepoPath("ops", "server", "grandumi-production-backend.service"),
            RepoPath("ops", "server", "grandumi-production-backend@.service"),
            RepoPath("ops", "server", "stage-grandumi-production.sh"),
            RepoPath("ops", "server", "promote-approved.sh"),
        };

        foreach (var file in files)
            Assert.DoesNotContain(
                "grandumi-test-replay-artifacts",
                File.ReadAllText(file),
                StringComparison.Ordinal);
    }

    [Fact]
    public void 生产Registry仍为空且自校验有效()
    {
        var path = RepoPath(
            "服务端WebSocket",
            "Training",
            "Artifacts",
            "replay-artifact-registry.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(0, document.RootElement.GetProperty("artifacts").GetArrayLength());
        _ = GrandUMI.Training.ReplayArtifactRegistry.Load(path);
    }

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "服务端WebSocket")))
                return Path.Combine([directory.FullName, .. parts]);
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法定位 GrandUMI 仓库根目录");
    }
}
