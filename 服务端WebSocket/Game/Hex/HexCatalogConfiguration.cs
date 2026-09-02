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
        HexTier.Silver => 18,
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

        var knownIds = HexCatalog.All.Select(item => item.Id).Order().ToArray();
        if (!values.Keys.Order().SequenceEqual(knownIds))
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
