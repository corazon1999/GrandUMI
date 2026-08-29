using System.Runtime.CompilerServices;
using GrandUMI.Training;

namespace GrandUMI.Tests;

internal static class GrandUmiTestTempPolicy
{
    private const string WindowsTempRoot = @"E:\GrandUMI-Temp\Tests";

    [ModuleInitializer]
    internal static void Initialize()
    {
        ReplayRuntimeIdentityProvider.Initialize(new ReplayRuntimeBuildIdentity(
            "1111111111111111111111111111111111111111",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"));

        if (!OperatingSystem.IsWindows()) return;

        var driveRoot = Path.GetPathRoot(WindowsTempRoot);
        if (string.IsNullOrWhiteSpace(driveRoot) || !Directory.Exists(driveRoot))
        {
            throw new InvalidOperationException(
                "E 盘不可用，拒绝在 C 盘或系统临时目录运行 GrandUMI 测试。");
        }

        Directory.CreateDirectory(WindowsTempRoot);
        Environment.SetEnvironmentVariable("TEMP", WindowsTempRoot, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("TMP", WindowsTempRoot, EnvironmentVariableTarget.Process);

        var resolvedTemp = Path.GetFullPath(Path.GetTempPath());
        var expectedRoot = Path.GetFullPath(@"E:\");
        if (!resolvedTemp.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"GrandUMI 测试临时目录必须位于 E 盘，实际解析为：{resolvedTemp}");
        }
    }
}
