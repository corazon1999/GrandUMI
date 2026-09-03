using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandUMI.Game.Hex;

public sealed record HexCatalogTierAssignment(int Id, HexTier Tier);

/// <summary>
/// 一份不可变的海克斯品质配置。Revision 只描述目标环境中的发布次序；Digest 才是内容身份。
/// </summary>
public sealed class HexCatalogConfiguration
{
    public const string Schema = "grandumi.hex-catalog.v1";
    public static int RequiredRegularHexes(HexTier tier) => tier switch
    {
        HexTier.Silver => 45,
        HexTier.Gold => 18,
        HexTier.Rainbow => 17,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知海克斯品质"),
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private HexCatalogConfiguration(
        long revision,
        string digest,
        IReadOnlyDictionary<int, HexTier> tiers,
        long? publishedAt,
        string? publishedBy,
        long? sourceDraftRevision,
        string? sourceRequestId)
    {
        Revision = revision;
        Digest = digest;
        Tiers = tiers;
        PublishedAt = publishedAt;
        PublishedBy = publishedBy;
        SourceDraftRevision = sourceDraftRevision;
        SourceRequestId = sourceRequestId;
    }

    public long Revision { get; }
    public string Digest { get; }
    public IReadOnlyDictionary<int, HexTier> Tiers { get; }
    public long? PublishedAt { get; }
    public string? PublishedBy { get; }
    public long? SourceDraftRevision { get; }
    public string? SourceRequestId { get; }

    public IReadOnlyList<HexCatalogTierAssignment> Assignments
        => Tiers.OrderBy(item => item.Key)
            .Select(item => new HexCatalogTierAssignment(item.Key, item.Value))
            .ToArray();

    public HexTier TierOf(int id)
        => Tiers.TryGetValue(id, out var tier)
            ? tier
            : throw new KeyNotFoundException($"海克斯配置缺少编号 {id}");

    public static HexCatalogConfiguration BuiltIn { get; } = Create(
        revision: 0,
        HexCatalog.All.Select(item => new HexCatalogTierAssignment(item.Id, item.Tier)));

    public static HexCatalogConfiguration BuiltInForRulesRevision(int rulesRevision)
        => rulesRevision >= HexRules.ExpansionRulesRevision
            ? BuiltIn
            : CreateForRulesRevision(
                rulesRevision,
                revision: 0,
                HexCatalog.All
                    .Where(item => item.Id <= 56)
                    .Select(item => new HexCatalogTierAssignment(item.Id, item.Tier)));

    public static HexCatalogConfiguration Create(
        long revision,
        IEnumerable<HexCatalogTierAssignment> assignments,
        string? expectedDigest = null,
        long? publishedAt = null,
        string? publishedBy = null,
        long? sourceDraftRevision = null,
        string? sourceRequestId = null)
        => CreateValidated(
            revision,
            assignments,
            requireBalancedPools: true,
            expectedDigest,
            publishedAt,
            publishedBy,
            sourceDraftRevision,
            sourceRequestId);

    public static HexCatalogConfiguration CreateDraft(
        IEnumerable<HexCatalogTierAssignment> assignments,
        string? expectedDigest = null)
        => CreateValidated(
            revision: 0,
            assignments,
            requireBalancedPools: false,
            expectedDigest);

    /// <summary>
    /// 读取已经锁定到旧房间、录像或旧环境 active 文件中的完整目录。
    /// 历史配置创建时已按当时池规模校验；升级后只校验编号与摘要，不用新池规模反向否定历史数据。
    /// </summary>
    public static HexCatalogConfiguration CreateHistoricalSnapshot(
        long revision,
        IEnumerable<HexCatalogTierAssignment> assignments,
        string? expectedDigest = null,
        long? publishedAt = null,
        string? publishedBy = null,
        long? sourceDraftRevision = null,
        string? sourceRequestId = null)
        => CreateValidated(
            revision,
            assignments,
            requireBalancedPools: false,
            expectedDigest,
            publishedAt,
            publishedBy,
            sourceDraftRevision,
            sourceRequestId);

    private static HexCatalogConfiguration CreateValidated(
        long revision,
        IEnumerable<HexCatalogTierAssignment> assignments,
        bool requireBalancedPools,
        string? expectedDigest = null,
        long? publishedAt = null,
        string? publishedBy = null,
        long? sourceDraftRevision = null,
        string? sourceRequestId = null)
    {
        if (revision < 0) throw new InvalidDataException("海克斯配置版本不能小于 0。");
        ArgumentNullException.ThrowIfNull(assignments);
        var values = new Dictionary<int, HexTier>();
        foreach (var assignment in assignments)
        {
            if (!Enum.IsDefined(assignment.Tier))
                throw new InvalidDataException($"海克斯 {assignment.Id} 的品质无效。");
            if (!values.TryAdd(assignment.Id, assignment.Tier))
                throw new InvalidDataException($"海克斯配置重复包含编号 {assignment.Id}。");
        }

        var currentIds = HexCatalog.All.Select(item => item.Id).Order().ToArray();
        var legacyIds = HexCatalog.All.Where(item => item.Id <= 56).Select(item => item.Id).Order().ToArray();
        if (values.Keys.Order().SequenceEqual(legacyIds))
        {
            // 升级前发布的 active 文件只有 1..56。若文件携带旧摘要，先按旧内容验真，
            // 再只在内存中补入本修订新增项；下一次正常发布会把完整新目录原子写回。
            if (expectedDigest is not null
                && !string.Equals(expectedDigest, ComputeDigest(values), StringComparison.Ordinal))
                throw new InvalidDataException("海克斯配置摘要校验失败。");
            foreach (var definition in HexCatalog.All.Where(item => item.Id > 56))
                values.Add(definition.Id, definition.Tier);
            expectedDigest = null;
        }
        if (!values.Keys.Order().SequenceEqual(currentIds))
            throw new InvalidDataException("海克斯配置必须且只能包含完整的权威目录编号。");

        if (requireBalancedPools)
        {
            var regularIds = HexCatalog.Regular.Select(item => item.Id).ToHashSet();
            foreach (var tier in Enum.GetValues<HexTier>())
            {
                var count = values.Count(item => regularIds.Contains(item.Key) && item.Value == tier);
                var required = RequiredRegularHexes(tier);
                if (count != required)
                    throw new InvalidDataException(
                        $"{HexCatalog.TierDisplayName(tier)}常规海克斯必须恰好为 {required} 个，当前为 {count} 个。");
            }
        }

        var digest = ComputeDigest(values);
        if (expectedDigest is not null
            && !string.Equals(expectedDigest, digest, StringComparison.Ordinal))
            throw new InvalidDataException("海克斯配置摘要校验失败。");

        return new HexCatalogConfiguration(
            revision,
            digest,
            new ReadOnlyDictionary<int, HexTier>(values),
            publishedAt,
            string.IsNullOrWhiteSpace(publishedBy) ? null : publishedBy.Trim(),
            sourceDraftRevision,
            string.IsNullOrWhiteSpace(sourceRequestId) ? null : sourceRequestId.Trim());
    }

    /// <summary>
    /// 恢复历史房间时按建局规则版本验证当时的完整目录，不把新增编号补进旧房间，
    /// 也不重算其已经写入日志的摘要。
    /// </summary>
    public static HexCatalogConfiguration CreateForRulesRevision(
        int rulesRevision,
        long revision,
        IEnumerable<HexCatalogTierAssignment> assignments,
        string? expectedDigest = null)
    {
        if (rulesRevision is < HexRules.LegacyRulesRevision or > HexRules.CurrentRulesRevision)
            throw new InvalidDataException($"不支持的海克斯规则版本：{rulesRevision}");
        if (revision < 0) throw new InvalidDataException("海克斯配置版本不能小于 0。");
        ArgumentNullException.ThrowIfNull(assignments);
        var values = new Dictionary<int, HexTier>();
        foreach (var assignment in assignments)
        {
            if (!Enum.IsDefined(assignment.Tier))
                throw new InvalidDataException($"海克斯 {assignment.Id} 的品质无效。");
            if (!values.TryAdd(assignment.Id, assignment.Tier))
                throw new InvalidDataException($"海克斯配置重复包含编号 {assignment.Id}。");
        }

        var expectedIds = HexCatalog.All
            .Where(item => rulesRevision >= HexRules.ExpansionRulesRevision || item.Id <= 56)
            .Select(item => item.Id)
            .Order()
            .ToArray();
        if (!values.Keys.Order().SequenceEqual(expectedIds))
            throw new InvalidDataException("海克斯配置与建局规则版本的权威目录不一致。");
        ValidateNonEmptyRegularPools(values, rulesRevision);
        var digest = ComputeDigest(values);
        if (expectedDigest is not null && !string.Equals(expectedDigest, digest, StringComparison.Ordinal))
            throw new InvalidDataException("海克斯配置摘要校验失败。");
        return new HexCatalogConfiguration(
            revision,
            digest,
            new ReadOnlyDictionary<int, HexTier>(values),
            null,
            null,
            null,
            null);
    }

    public static HexCatalogConfiguration ReadActiveFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schema", out var schema)
            || schema.GetString() != Schema)
            throw new InvalidDataException("海克斯激活配置 schema 无效。");
        if (!root.TryGetProperty("revision", out var revisionElement)
            || !revisionElement.TryGetInt64(out var revision))
            throw new InvalidDataException("海克斯激活配置缺少有效版本。");
        if (!root.TryGetProperty("digest", out var digestElement)
            || digestElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("海克斯激活配置缺少摘要。");
        if (!root.TryGetProperty("tiers", out var tiersElement)
            || tiersElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("海克斯激活配置缺少品质列表。");

        var assignments = tiersElement.EnumerateArray().Select(ReadAssignment).ToArray();
        return CreateHistoricalSnapshot(
            revision,
            assignments,
            digestElement.GetString(),
            OptionalInt64(root, "publishedAt"),
            OptionalString(root, "publishedBy"),
            OptionalInt64(root, "sourceDraftRevision"),
            OptionalString(root, "sourceRequestId"));
    }

    public static byte[] SerializeActive(HexCatalogConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = Schema,
            revision = configuration.Revision,
            digest = configuration.Digest,
            sourceDraftRevision = configuration.SourceDraftRevision,
            sourceRequestId = configuration.SourceRequestId,
            publishedAt = configuration.PublishedAt,
            publishedBy = configuration.PublishedBy,
            tiers = configuration.Assignments,
        }, JsonOptions);
    }

    public static string ComputeDigest(IEnumerable<HexCatalogTierAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var values = assignments.ToDictionary(item => item.Id, item => item.Tier);
        return ComputeDigest(values);
    }

    private static string ComputeDigest(IReadOnlyDictionary<int, HexTier> assignments)
    {
        var canonical = string.Concat(assignments.OrderBy(item => item.Key)
            .Select(item => $"{item.Key}:{item.Value}\n"));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static void ValidateNonEmptyRegularPools(
        IReadOnlyDictionary<int, HexTier> assignments,
        int rulesRevision)
    {
        var regularIds = HexCatalog.RegularForRevision(rulesRevision)
            .Select(item => item.Id)
            .ToHashSet();
        foreach (var tier in Enum.GetValues<HexTier>())
        {
            if (!assignments.Any(item => regularIds.Contains(item.Key) && item.Value == tier))
                throw new InvalidDataException($"{HexCatalog.TierDisplayName(tier)}常规海克斯池不能为空。");
        }
    }

    private static HexCatalogTierAssignment ReadAssignment(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var id)
            || !element.TryGetProperty("tier", out var tierElement)
            || tierElement.ValueKind != JsonValueKind.String
            || !Enum.TryParse<HexTier>(tierElement.GetString(), ignoreCase: false, out var tier))
            throw new InvalidDataException("海克斯激活配置包含无效品质项。");
        return new HexCatalogTierAssignment(id, tier);
    }

    private static long? OptionalInt64(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var result)
            ? result
            : null;

    private static string? OptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// 新房间读取 active 文件；读取结果随后复制进 GameState，运行中的房间绝不再次访问热配置。
/// </summary>
public static class HexCatalogRuntime
{
    private static string? _activePath;

    public static void Configure(string activePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activePath);
        var fullPath = Path.GetFullPath(activePath);
        if (File.Exists(fullPath)) _ = HexCatalogConfiguration.ReadActiveFile(fullPath);
        Volatile.Write(ref _activePath, fullPath);
    }

    public static HexCatalogConfiguration SnapshotForNewRoom()
    {
        var path = Volatile.Read(ref _activePath);
        return path is not null && File.Exists(path)
            ? HexCatalogConfiguration.ReadActiveFile(path)
            : HexCatalogConfiguration.BuiltIn;
    }
}
