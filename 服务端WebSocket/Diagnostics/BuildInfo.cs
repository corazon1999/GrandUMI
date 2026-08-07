using System.Reflection;

namespace GrandUMI.Diagnostics;

public static class BuildInfo
{
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "unknown";

    public static string Commit { get; } =
        Environment.GetEnvironmentVariable("GRANDUMI_BUILD_COMMIT")?.Trim()
        ?? ExtractCommit(Version)
        ?? "unknown";

    public static string BuildTimeUtc { get; } =
        Environment.GetEnvironmentVariable("GRANDUMI_BUILD_TIME_UTC")?.Trim()
        ?? File.GetLastWriteTimeUtc(Assembly.GetEntryAssembly()?.Location ?? AppContext.BaseDirectory)
            .ToString("O");

    public static string NodeId { get; } =
        Environment.GetEnvironmentVariable("GRANDUMI_NODE_ID")?.Trim()
        ?? $"{Environment.MachineName}-{Environment.ProcessId}";

    private static string? ExtractCommit(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 && plus + 1 < version.Length ? version[(plus + 1)..] : null;
    }
}
