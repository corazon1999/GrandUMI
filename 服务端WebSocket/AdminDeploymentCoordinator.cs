using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrandUMI.Game.Hex;

namespace GrandUMI;

public sealed record AdminDeploymentStatus(
    string Environment,
    string State,
    string? TargetCommit,
    string? DeployedCommit,
    string Message,
    long? UpdatedAt);

public sealed record AdminHexCatalogDeploymentStatus(
    string Environment,
    string State,
    string? TargetDigest,
    string Message,
    long? UpdatedAt);

public sealed record AdminHexCatalogDraft(
    string Environment,
    long DraftRevision,
    long BaseActiveRevision,
    string BaseActiveDigest,
    string Digest,
    IReadOnlyList<HexCatalogTierAssignment> Assignments,
    long? SavedAt,
    string? SavedBy,
    string? LastRequestId);

public sealed record AdminHexCatalogEnvironmentState(
    string Environment,
    HexCatalogConfiguration Active,
    AdminHexCatalogDraft Draft,
    AdminHexCatalogDeploymentStatus Deployment);

public sealed record AdminHexCatalogSaveResult(
    AdminHexCatalogEnvironmentState State,
    bool Replayed);

/// <summary>
/// 管理面板与 root 发布执行器之间的受限文件队列。
/// 后端只能写请求与共享草稿目录；实际部署和目标环境 active 文件替换均由 systemd 固定脚本执行。
/// </summary>
public sealed class AdminDeploymentCoordinator
{
    private const string DraftSchema = "grandumi.admin.hex-catalog-draft.v1";
    private const string RequestSchema = "grandumi.admin.hex-catalog-request.v1";
    private static readonly HashSet<string> Environments = new(StringComparer.Ordinal) { "test", "production" };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _rootDirectory;
    private readonly string _requestDirectory;
    private readonly string _statusDirectory;
    private readonly string _draftDirectory;
    private readonly string? _environmentDataRoot;

    public AdminDeploymentCoordinator(string rootDirectory, string? environmentDataRoot = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("管理员发布目录不能为空。", nameof(rootDirectory));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _requestDirectory = Path.Combine(_rootDirectory, "requests");
        _statusDirectory = Path.Combine(_rootDirectory, "status");
        _draftDirectory = Path.Combine(_rootDirectory, "drafts");
        _environmentDataRoot = string.IsNullOrWhiteSpace(environmentDataRoot)
            ? null
            : Path.GetFullPath(environmentDataRoot);
    }

    public static AdminDeploymentCoordinator? FromEnvironment()
    {
        var directory = Environment.GetEnvironmentVariable("GRANDUMI_ADMIN_DEPLOY_DIR");
        return string.IsNullOrWhiteSpace(directory) ? null : new AdminDeploymentCoordinator(directory);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_requestDirectory);
        Directory.CreateDirectory(_draftDirectory);
        if (!Directory.Exists(_statusDirectory))
            throw new InvalidOperationException("管理员发布状态目录尚未由服务器初始化。");
    }

    public AdminDeploymentStatus Queue(string environment)
    {
        ValidateEnvironment(environment);
        using var queueLock = AcquireLock(Path.Combine(_requestDirectory, ".queue.lock"));
        EnsureQueueEmpty();

        var nonce = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(_requestDirectory, $"{environment}-{nonce}.request");
        WriteAtomic(finalPath, Encoding.UTF8.GetBytes($"environment={environment}\nnonce={nonce}\n"));
        return new AdminDeploymentStatus(environment, "queued", null, ReadDeployedCommit(environment), "发布任务已进入安全队列。", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public AdminDeploymentStatus GetStatus(string environment)
    {
        ValidateEnvironment(environment);
        var values = ReadStatusValues(Path.Combine(_statusDirectory, $"{environment}.status"));
        var state = values.GetValueOrDefault("state") ?? "idle";
        var message = Decode(values.GetValueOrDefault("message")) ?? (state == "idle" ? "尚未执行面板发布任务。" : "正在读取发布状态。");
        return new AdminDeploymentStatus(
            environment,
            state,
            EmptyToNull(values.GetValueOrDefault("target")),
            ReadDeployedCommit(environment),
            message,
            ReadUpdatedAt(values));
    }

    public AdminHexCatalogEnvironmentState GetHexCatalogState(string environment)
    {
        ValidateEnvironment(environment);
        var active = ReadActiveHexCatalog(environment);
        var draft = ReadDraft(environment) ?? DraftFromActive(environment, active);
        return new AdminHexCatalogEnvironmentState(
            environment,
            active,
            draft,
            GetHexCatalogDeploymentStatus(environment));
    }

    public AdminHexCatalogSaveResult SaveHexCatalogDraft(
        string environment,
        long expectedDraftRevision,
        long expectedActiveRevision,
        IReadOnlyList<HexCatalogTierAssignment> assignments,
        string actor,
        string requestId)
    {
        ValidateEnvironment(environment);
        actor = RequiredToken(actor, 64, "操作者");
        requestId = RequiredToken(requestId, 128, "请求编号");
        ArgumentNullException.ThrowIfNull(assignments);

        using var draftLock = AcquireLock(DraftLockPath(environment));
        var active = ReadActiveHexCatalog(environment);
        var current = ReadDraft(environment) ?? DraftFromActive(environment, active);
        var requested = HexCatalogConfiguration.CreateDraft(
            CompleteRetiredHexAssignments(assignments, current.Assignments));
        if (string.Equals(current.LastRequestId, requestId, StringComparison.Ordinal))
        {
            if (!string.Equals(current.Digest, requested.Digest, StringComparison.Ordinal))
                throw new InvalidOperationException("同一请求编号对应了不同的海克斯草稿内容。");
            return new AdminHexCatalogSaveResult(GetHexCatalogState(environment), Replayed: true);
        }
        if (current.DraftRevision != expectedDraftRevision)
            throw new InvalidOperationException(
                $"海克斯草稿已由其他管理员更新（当前 v{current.DraftRevision}），请刷新后重试。");
        if (active.Revision != expectedActiveRevision)
            throw new InvalidOperationException(
                $"{EnvironmentName(environment)}已发布配置已变化（当前 v{active.Revision}），请刷新后重新保存。");

        var savedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var next = new AdminHexCatalogDraft(
            environment,
            checked(current.DraftRevision + 1),
            active.Revision,
            active.Digest,
            requested.Digest,
            requested.Assignments,
            savedAt,
            actor,
            requestId);
        WriteDraft(next);
        return new AdminHexCatalogSaveResult(
            new AdminHexCatalogEnvironmentState(
                environment,
                active,
                next,
                GetHexCatalogDeploymentStatus(environment)),
            Replayed: false);
    }

    public AdminHexCatalogEnvironmentState QueueHexCatalog(
        string environment,
        long draftRevision,
        string draftDigest,
        string actor,
        string requestId)
    {
        ValidateEnvironment(environment);
        actor = RequiredToken(actor, 64, "操作者");
        requestId = RequiredToken(requestId, 128, "请求编号");
        draftDigest = RequiredToken(draftDigest, 80, "草稿摘要");

        using var draftLock = AcquireLock(DraftLockPath(environment));
        var active = ReadActiveHexCatalog(environment);
        var draft = ReadDraft(environment) ?? throw new InvalidOperationException("尚未保存海克斯草稿，不能发布。");
        if (draft.DraftRevision != draftRevision
            || !string.Equals(draft.Digest, draftDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("海克斯草稿已变化，请刷新并重新申请确认凭证。");
        if (draft.BaseActiveRevision != active.Revision
            || !string.Equals(draft.BaseActiveDigest, active.Digest, StringComparison.Ordinal))
            throw new InvalidOperationException("目标环境的已发布配置已变化，请重新保存草稿后再发布。");
        if (string.Equals(draft.Digest, active.Digest, StringComparison.Ordinal))
            throw new InvalidOperationException("草稿与目标环境当前配置一致，无需重复发布。");

        _ = HexCatalogConfiguration.Create(0, draft.Assignments, draft.Digest);

        using var queueLock = AcquireLock(Path.Combine(_requestDirectory, ".queue.lock"));
        EnsureQueueEmpty();
        var nonce = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(_requestDirectory, $"hex-{environment}-{nonce}.request");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = RequestSchema,
            kind = "hex-catalog",
            environment,
            nonce,
            actor,
            requestId,
            draftRevision = draft.DraftRevision,
            expectedActiveRevision = active.Revision,
            expectedActiveDigest = active.Digest,
            digest = draft.Digest,
            tiers = draft.Assignments,
        }, JsonOptions);
        WriteAtomic(finalPath, payload);

        var deployment = new AdminHexCatalogDeploymentStatus(
            environment,
            "queued",
            draft.Digest,
            "海克斯配置发布任务已进入安全队列。",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return new AdminHexCatalogEnvironmentState(environment, active, draft, deployment);
    }

    public static string HexCatalogApprovalTarget(string environment, long draftRevision, string digest)
    {
        ValidateEnvironment(environment);
        digest = RequiredToken(digest, 80, "草稿摘要");
        return $"{environment}:draft-{draftRevision}:{digest}";
    }

    private AdminHexCatalogDeploymentStatus GetHexCatalogDeploymentStatus(string environment)
    {
        var values = ReadStatusValues(Path.Combine(_statusDirectory, $"hex-{environment}.status"));
        var state = values.GetValueOrDefault("state") ?? "idle";
        if (state != "running"
            && Directory.EnumerateFiles(_requestDirectory, $"hex-{environment}-*.request").Any())
            state = "queued";
        var message = Decode(values.GetValueOrDefault("message"))
            ?? (state == "queued" ? "海克斯配置发布任务正在等待执行。" : "尚未发布面板海克斯配置。");
        return new AdminHexCatalogDeploymentStatus(
            environment,
            state,
            EmptyToNull(values.GetValueOrDefault("target")),
            message,
            ReadUpdatedAt(values));
    }

    private HexCatalogConfiguration ReadActiveHexCatalog(string environment)
    {
        var path = ActiveHexCatalogPath(environment);
        return File.Exists(path)
            ? HexCatalogConfiguration.ReadActiveFile(path)
            : HexCatalogConfiguration.BuiltIn;
    }

    private string ActiveHexCatalogPath(string environment)
    {
        if (_environmentDataRoot is not null)
            return Path.Combine(_environmentDataRoot, environment, "hex-catalog", "active.json");
        if (OperatingSystem.IsWindows())
            return Path.Combine(_rootDirectory, "runtime", environment, "hex-catalog", "active.json");
        return environment == "test"
            ? "/data/grandumi-test/hex-catalog/active.json"
            : "/data/grandumi/hex-catalog/active.json";
    }

    private AdminHexCatalogDraft? ReadDraft(string environment)
    {
        var path = DraftPath(environment);
        if (!File.Exists(path)) return null;
        var document = JsonSerializer.Deserialize<HexDraftDocument>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("海克斯草稿文件为空。");
        if (document.Schema != DraftSchema || document.Environment != environment)
            throw new InvalidDataException("海克斯草稿文件 schema 或环境无效。");
        var validated = HexCatalogConfiguration.CreateDraft(document.Tiers, document.Digest);
        if (document.DraftRevision < 1 || document.BaseActiveRevision < 0)
            throw new InvalidDataException("海克斯草稿版本无效。");
        return new AdminHexCatalogDraft(
            environment,
            document.DraftRevision,
            document.BaseActiveRevision,
            document.BaseActiveDigest,
            validated.Digest,
            validated.Assignments,
            document.SavedAt,
            document.SavedBy,
            document.LastRequestId);
    }

    private void WriteDraft(AdminHexCatalogDraft draft)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new HexDraftDocument(
            DraftSchema,
            draft.Environment,
            draft.DraftRevision,
            draft.BaseActiveRevision,
            draft.BaseActiveDigest,
            draft.Digest,
            draft.SavedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            draft.SavedBy ?? "unknown",
            draft.LastRequestId ?? string.Empty,
            draft.Assignments.ToArray()), JsonOptions);
        WriteAtomic(DraftPath(draft.Environment), payload);
    }

    private static AdminHexCatalogDraft DraftFromActive(string environment, HexCatalogConfiguration active)
        => new(
            environment,
            DraftRevision: 0,
            active.Revision,
            active.Digest,
            active.Digest,
            active.Assignments,
            SavedAt: null,
            SavedBy: null,
            LastRequestId: null);

    private static IReadOnlyList<HexCatalogTierAssignment> CompleteRetiredHexAssignments(
        IReadOnlyList<HexCatalogTierAssignment> assignments,
        IReadOnlyList<HexCatalogTierAssignment> baseline)
    {
        var knownIds = HexCatalog.All.Select(item => item.Id).ToHashSet();
        var configurableIds = HexCatalog.Configurable.Select(item => item.Id).ToHashSet();
        var supplied = new Dictionary<int, HexTier>();
        foreach (var assignment in assignments)
        {
            if (!knownIds.Contains(assignment.Id))
                throw new InvalidDataException($"海克斯草稿包含未知编号 {assignment.Id}。");
            if (!supplied.TryAdd(assignment.Id, assignment.Tier))
                throw new InvalidDataException($"海克斯草稿重复包含编号 {assignment.Id}。");
        }
        if (!configurableIds.SetEquals(supplied.Keys.Where(configurableIds.Contains)))
            throw new InvalidDataException("海克斯草稿必须包含完整的可调配目录。");

        var baselineById = baseline.ToDictionary(item => item.Id, item => item.Tier);
        foreach (var retired in HexCatalog.All.Where(item => HexCatalog.IsRetired(item.Id)))
            // 新旧客户端即使提交了退役项，也只能沿用当前基线，不能借完整目录协议改写它。
            supplied[retired.Id] = baselineById[retired.Id];
        return supplied.OrderBy(item => item.Key)
            .Select(item => new HexCatalogTierAssignment(item.Key, item.Value))
            .ToArray();
    }

    private string DraftPath(string environment) => Path.Combine(_draftDirectory, $"hex-{environment}.json");
    private string DraftLockPath(string environment) => Path.Combine(_draftDirectory, $".hex-{environment}.lock");

    private void EnsureQueueEmpty()
    {
        if (Directory.EnumerateFiles(_requestDirectory, "*.request").Any())
            throw new InvalidOperationException("已有发布任务正在排队，请等待当前任务完成。");
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

    private static Dictionary<string, string> ReadStatusValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator]] = line[(separator + 1)..];
        }
        return values;
    }

    private static long? ReadUpdatedAt(IReadOnlyDictionary<string, string> values)
        => long.TryParse(values.GetValueOrDefault("updated"), out var updatedSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(updatedSeconds).ToUnixTimeMilliseconds()
            : null;

    private static string? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch (FormatException) { return "发布状态信息格式无效。"; }
    }

    private static FileStream AcquireLock(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static void WriteAtomic(string finalPath, byte[] payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(finalPath)!,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static string RequiredToken(string value, int maximumLength, string label)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is 0 || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"{label}格式无效。", label);
        return value;
    }

    private static string EnvironmentName(string environment) => environment == "production" ? "正式服" : "测试服";
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateEnvironment(string environment)
    {
        if (!Environments.Contains(environment)) throw new ArgumentException("仅支持 test 或 production。", nameof(environment));
    }

    private sealed record HexDraftDocument(
        string Schema,
        string Environment,
        long DraftRevision,
        long BaseActiveRevision,
        string BaseActiveDigest,
        string Digest,
        long SavedAt,
        string SavedBy,
        string LastRequestId,
        HexCatalogTierAssignment[] Tiers);
}
