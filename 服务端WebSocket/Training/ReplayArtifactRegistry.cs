using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrandUMI.Training;

/// <summary>注册表格式或自校验哈希无效。</summary>
public sealed class ReplayArtifactRegistryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>一份只能按精确身份选择、不能回退到当前 main 的重放工件。</summary>
public sealed record ReplayArtifactDescriptor(
    string MatchLogSchema,
    string EventAdapterVersion,
    string EngineArtifactId,
    string EngineCommit,
    string BinarySha256,
    string RulesVersion,
    string RulesetManifestHash,
    string CardDbContentHash,
    string RngAlgorithmVersion,
    string DeterministicIdVersion,
    string OpeningProtocolVersion,
    string ReplayConfigSchema,
    string Executable);

/// <summary>跨进程 worker 路由使用的完整工件身份指纹。</summary>
public static class ReplayArtifactIdentity
{
    public static string Fingerprint(ReplayArtifactDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var canonical = JsonSerializer.SerializeToElement(new
        {
            descriptor.MatchLogSchema,
            descriptor.EventAdapterVersion,
            descriptor.EngineArtifactId,
            descriptor.EngineCommit,
            descriptor.BinarySha256,
            descriptor.RulesVersion,
            descriptor.RulesetManifestHash,
            descriptor.CardDbContentHash,
            descriptor.RngAlgorithmVersion,
            descriptor.DeterministicIdVersion,
            descriptor.OpeningProtocolVersion,
            descriptor.ReplayConfigSchema,
            descriptor.Executable,
        });
        return CanonicalJson.Hash(canonical);
    }
}

/// <summary>
/// P0-0 不可变工件注册表。这里只解析和解析身份，不直接启动 executable；artifact worker
/// dispatcher 另行按完整 descriptor 指纹绑定允许的进程内或进程代理实现。
/// </summary>
public sealed class ReplayArtifactRegistry
{
    public const string Schema = "grandumi.replay_artifact_registry.v1";
    private static readonly Regex Sha256Pattern = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex CommitPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant);
    private static readonly Regex ArtifactIdPattern = new("^[a-z0-9][a-z0-9._-]{2,127}$", RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, ReplayArtifactDescriptor> _byArtifactId;

    private ReplayArtifactRegistry(
        string registryVersion,
        string registryHash,
        IReadOnlyList<ReplayArtifactDescriptor> artifacts)
    {
        RegistryVersion = registryVersion;
        RegistryHash = registryHash;
        Artifacts = artifacts;
        _byArtifactId = artifacts.ToDictionary(
            artifact => artifact.EngineArtifactId,
            StringComparer.Ordinal);
    }

    public string RegistryVersion { get; }
    public string RegistryHash { get; }
    public IReadOnlyList<ReplayArtifactDescriptor> Artifacts { get; }

    public static ReplayArtifactRegistry Load(string path)
        => Parse(File.ReadAllText(path));

    public static ReplayArtifactRegistry Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            var root = document.RootElement;
            RequireObject(root, "注册表根节点");
            EnsureKnownProperties(root, "注册表根节点",
                "schema", "registryVersion", "registryHash", "artifacts");

            var schema = RequiredString(root, "schema", "注册表根节点");
            if (!string.Equals(schema, Schema, StringComparison.Ordinal))
                throw new ReplayArtifactRegistryException($"不支持的工件注册表 schema：{schema}");

            var registryVersion = RequiredString(root, "registryVersion", "注册表根节点");
            var declaredHash = RequiredString(root, "registryHash", "注册表根节点");
            RequireSha256(declaredHash, "registryHash");
            var computedHash = CanonicalJson.Hash(root, excludedTopLevelProperty: "registryHash");
            if (!string.Equals(declaredHash, computedHash, StringComparison.Ordinal))
                throw new ReplayArtifactRegistryException(
                    $"注册表自校验哈希不一致：声明 {declaredHash}，计算 {computedHash}");

            if (!root.TryGetProperty("artifacts", out var artifactsElement)
                || artifactsElement.ValueKind != JsonValueKind.Array)
                throw new ReplayArtifactRegistryException("注册表 artifacts 必须是数组");

            var artifacts = new List<ReplayArtifactDescriptor>();
            string? previousArtifactId = null;
            foreach (var element in artifactsElement.EnumerateArray())
            {
                var descriptor = ParseDescriptor(element);
                if (previousArtifactId is not null
                    && string.CompareOrdinal(previousArtifactId, descriptor.EngineArtifactId) >= 0)
                    throw new ReplayArtifactRegistryException("artifacts 必须按 engineArtifactId 严格升序且不得重复");
                previousArtifactId = descriptor.EngineArtifactId;
                artifacts.Add(descriptor);
            }

            return new ReplayArtifactRegistry(
                registryVersion,
                declaredHash,
                Array.AsReadOnly(artifacts.ToArray()));
        }
        catch (ReplayArtifactRegistryException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
        {
            throw new ReplayArtifactRegistryException($"工件注册表无法解析：{ex.Message}", ex);
        }
    }

    public ReplayArtifactDescriptor Resolve(ReplayVersionIdentity identity)
    {
        if (!_byArtifactId.TryGetValue(identity.EngineArtifactId, out var descriptor))
            throw new ReplayQuarantineException(
                ReplayQuarantineCodes.UnsupportedArtifact,
                "artifact_registry",
                $"engineArtifactId 未登记：{identity.EngineArtifactId}");

        var mismatches = new List<string>();
        Compare("matchLogSchema", identity.MatchLogSchema, descriptor.MatchLogSchema, mismatches);
        Compare("engineCommit", identity.EngineCommit, descriptor.EngineCommit, mismatches);
        Compare("rulesVersion", identity.RulesVersion, descriptor.RulesVersion, mismatches);
        Compare("rulesetManifestHash", identity.RulesetManifestHash, descriptor.RulesetManifestHash, mismatches);
        Compare("cardDbContentHash", identity.CardDbContentHash, descriptor.CardDbContentHash, mismatches);
        Compare("rngAlgorithmVersion", identity.RngAlgorithmVersion, descriptor.RngAlgorithmVersion, mismatches);
        Compare("deterministicIdVersion", identity.DeterministicIdVersion, descriptor.DeterministicIdVersion, mismatches);
        Compare("openingProtocolVersion", identity.OpeningProtocolVersion, descriptor.OpeningProtocolVersion, mismatches);
        Compare("replayConfigSchema", identity.ReplayConfigSchema, descriptor.ReplayConfigSchema, mismatches);
        if (mismatches.Count > 0)
            throw new ReplayQuarantineException(
                ReplayQuarantineCodes.ArtifactIdentityMismatch,
                "artifact_registry",
                $"日志版本身份与注册工件不一致：{string.Join(", ", mismatches)}");
        return descriptor;
    }

    private static ReplayArtifactDescriptor ParseDescriptor(JsonElement element)
    {
        RequireObject(element, "artifact");
        EnsureKnownProperties(element, "artifact",
            "matchLogSchema", "eventAdapterVersion", "engineArtifactId", "engineCommit",
            "binarySha256", "rulesVersion", "rulesetManifestHash", "cardDbContentHash",
            "rngAlgorithmVersion", "deterministicIdVersion", "openingProtocolVersion",
            "replayConfigSchema", "executable");

        var descriptor = new ReplayArtifactDescriptor(
            RequiredString(element, "matchLogSchema", "artifact"),
            RequiredString(element, "eventAdapterVersion", "artifact"),
            RequiredString(element, "engineArtifactId", "artifact"),
            RequiredString(element, "engineCommit", "artifact"),
            RequiredString(element, "binarySha256", "artifact"),
            RequiredString(element, "rulesVersion", "artifact"),
            RequiredString(element, "rulesetManifestHash", "artifact"),
            RequiredString(element, "cardDbContentHash", "artifact"),
            RequiredString(element, "rngAlgorithmVersion", "artifact"),
            RequiredString(element, "deterministicIdVersion", "artifact"),
            RequiredString(element, "openingProtocolVersion", "artifact"),
            RequiredString(element, "replayConfigSchema", "artifact"),
            RequiredString(element, "executable", "artifact"));

        if (!ArtifactIdPattern.IsMatch(descriptor.EngineArtifactId))
            throw new ReplayArtifactRegistryException($"engineArtifactId 格式无效：{descriptor.EngineArtifactId}");
        if (!CommitPattern.IsMatch(descriptor.EngineCommit))
            throw new ReplayArtifactRegistryException("engineCommit 必须是完整的小写 Git 对象 ID");
        RequireSha256(descriptor.BinarySha256, "binarySha256");
        RequireSha256(descriptor.RulesetManifestHash, "rulesetManifestHash");
        RequireSha256(descriptor.CardDbContentHash, "cardDbContentHash");
        return descriptor;
    }

    private static void Compare(string field, string actual, string expected, ICollection<string> mismatches)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            mismatches.Add(field);
    }

    private static void RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ReplayArtifactRegistryException($"{context} 必须是 JSON 对象");
    }

    private static string RequiredString(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || property.GetString() is not { Length: > 0 } value
            || string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ReplayArtifactRegistryException($"{context}.{propertyName} 必须是无首尾空白的非空字符串");
        return value;
    }

    private static void RequireSha256(string value, string field)
    {
        if (!Sha256Pattern.IsMatch(value))
            throw new ReplayArtifactRegistryException($"{field} 必须是小写 sha256: 加 64 位十六进制");
    }

    private static void EnsureKnownProperties(JsonElement element, string context, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ReplayArtifactRegistryException($"{context} 包含重复属性：{property.Name}");
            if (!allowedSet.Contains(property.Name))
                throw new ReplayArtifactRegistryException($"{context} 包含未冻结属性：{property.Name}");
        }
    }
}
