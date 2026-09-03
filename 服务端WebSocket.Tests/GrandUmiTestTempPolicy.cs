using System.Runtime.CompilerServices;
using GrandUMI.Diagnostics;
using GrandUMI.Training;

namespace GrandUMI.Tests;

internal static class GrandUmiTestTempPolicy
{
    private const string TestTempRootVariable = "GRANDUMI_TEST_TEMP_ROOT";
    private const string WindowsTempBase = @"E:\GrandUMI-Temp";

    [ModuleInitializer]
    internal static void Initialize()
    {
        ServerCapacity.SetMemoryPressureProviderForTesting(
            static () => new ServerCapacity.MemoryPressureSnapshot(
                MemoryLoadBytes: 0,
                HighMemoryLoadThresholdBytes: 1));

        ReplayRuntimeIdentityProvider.Initialize(new ReplayRuntimeBuildIdentity(
            "1111111111111111111111111111111111111111",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"));

        if (!OperatingSystem.IsWindows()) return;

        var driveRoot = Path.GetPathRoot(WindowsTempBase);
        if (string.IsNullOrWhiteSpace(driveRoot) || !Directory.Exists(driveRoot))
        {
            throw new InvalidOperationException(
                "E 盘不可用，拒绝在 C 盘或系统临时目录运行 GrandUMI 测试。");
        }

        var configuredRoot = Environment.GetEnvironmentVariable(TestTempRootVariable);
        var tempRoot = ResolveWindowsTempRoot(
            configuredRoot,
            Environment.ProcessId,
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable(
            TestTempRootVariable,
            tempRoot,
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("TEMP", tempRoot, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("TMP", tempRoot, EnvironmentVariableTarget.Process);

        var resolvedTemp = NormalizePath(Path.GetTempPath());
        if (!string.Equals(resolvedTemp, tempRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"GrandUMI 测试临时目录未解析到隔离根。预期：{tempRoot}；实际：{resolvedTemp}");
        }
    }

    internal static string ResolveWindowsTempRoot(
        string? configuredRoot,
        int processId,
        string fallbackToken)
    {
        string candidate;
        if (configuredRoot is not null)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot)
                || !string.Equals(configuredRoot, configuredRoot.Trim(), StringComparison.Ordinal)
                || configuredRoot.Any(char.IsControl)
                || !Path.IsPathFullyQualified(configuredRoot))
            {
                throw new InvalidOperationException(
                    $"{TestTempRootVariable} 必须是 E:\\GrandUMI-Temp 下的绝对目录。");
            }

            candidate = NormalizePath(configuredRoot);
        }
        else
        {
            if (processId <= 0
                || string.IsNullOrWhiteSpace(fallbackToken)
                || fallbackToken.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                throw new InvalidOperationException("无法生成测试主机唯一的 E 盘临时目录。");
            }

            candidate = NormalizePath(Path.Combine(
                WindowsTempBase,
                "Tests",
                $"testhost-{processId}-{fallbackToken}"));
        }

        var allowedRoot = NormalizePath(WindowsTempBase);
        var allowedPrefix = allowedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{TestTempRootVariable} 必须位于 {allowedRoot} 下，实际解析为：{candidate}");
        }

        return candidate;
    }

    private static string NormalizePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
