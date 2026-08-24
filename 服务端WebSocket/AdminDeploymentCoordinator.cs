using System.Text;

namespace GrandUMI;

public sealed record AdminDeploymentStatus(
    string Environment,
    string State,
    string? TargetCommit,
    string? DeployedCommit,
    string Message,
    long? UpdatedAt);

/// <summary>
/// 管理面板与 root 发布执行器之间的受限文件队列。
/// 后端只能写请求目录；实际命令由 systemd 以固定脚本执行。
/// </summary>
public sealed class AdminDeploymentCoordinator
{
    private static readonly HashSet<string> Environments = new(StringComparer.Ordinal) { "test", "production" };
    private readonly string _rootDirectory;
    private readonly string _requestDirectory;
    private readonly string _statusDirectory;

    public AdminDeploymentCoordinator(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("管理员发布目录不能为空。", nameof(rootDirectory));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _requestDirectory = Path.Combine(_rootDirectory, "requests");
        _statusDirectory = Path.Combine(_rootDirectory, "status");
    }

    public static AdminDeploymentCoordinator? FromEnvironment()
    {
        var directory = Environment.GetEnvironmentVariable("GRANDUMI_ADMIN_DEPLOY_DIR");
        return string.IsNullOrWhiteSpace(directory) ? null : new AdminDeploymentCoordinator(directory);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_requestDirectory);
        if (!Directory.Exists(_statusDirectory))
            throw new InvalidOperationException("管理员发布状态目录尚未由服务器初始化。");
    }

    public AdminDeploymentStatus Queue(string environment)
    {
        ValidateEnvironment(environment);
        if (Directory.EnumerateFiles(_requestDirectory, "*.request").Any())
            throw new InvalidOperationException("已有发布任务正在排队，请等待当前任务完成。");

        var nonce = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(_requestDirectory, $"{environment}-{nonce}.request");
        var tempPath = Path.Combine(_requestDirectory, $".{environment}-{nonce}.tmp");
        File.WriteAllText(tempPath, $"environment={environment}\nnonce={nonce}\n", new UTF8Encoding(false));
        File.Move(tempPath, finalPath);
        return new AdminDeploymentStatus(environment, "queued", null, ReadDeployedCommit(environment), "发布任务已进入安全队列。", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public AdminDeploymentStatus GetStatus(string environment)
    {
        ValidateEnvironment(environment);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(_statusDirectory, $"{environment}.status");
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                values[line[..separator]] = line[(separator + 1)..];
            }
        }

        var state = values.GetValueOrDefault("state") ?? "idle";
        var message = Decode(values.GetValueOrDefault("message")) ?? (state == "idle" ? "尚未执行面板发布任务。" : "正在读取发布状态。");
        long? updatedAt = long.TryParse(values.GetValueOrDefault("updated"), out var updatedSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(updatedSeconds).ToUnixTimeMilliseconds()
            : null;
        return new AdminDeploymentStatus(
            environment,
            state,
            EmptyToNull(values.GetValueOrDefault("target")),
            ReadDeployedCommit(environment),
            message,
            updatedAt);
    }

    private string? ReadDeployedCommit(string environment)
    {
        var path = environment == "test"
            ? "/var/lib/grandumi-test-release/test-deployed"
            : "/var/lib/grandumi-production-deployed";
        if (OperatingSystem.IsWindows())
            path = Path.Combine(_rootDirectory, $"{environment}-deployed");
        try { return EmptyToNull(File.ReadAllText(path).Trim()); }
        catch { return null; }
    }

    private static string? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch (FormatException) { return "发布状态信息格式无效。"; }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateEnvironment(string environment)
    {
        if (!Environments.Contains(environment)) throw new ArgumentException("仅支持 test 或 production。", nameof(environment));
    }
}
