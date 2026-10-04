using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandUMI.Cards;

internal sealed record ValidatedCardContentManifest(
    IReadOnlyList<string> Files,
    int TotalCards,
    string ContentSha256,
    string? PlayabilityFile,
    IReadOnlySet<string> PendingCards);

/// <summary>
/// 启动时验证卡牌数据清单。任何缺失、额外、乱序、哈希或卡数不一致都直接阻止服务启动，
/// 避免部分卡集加载成功后继续对外提供不完整规则。
/// </summary>
internal static class CardContentManifest
{
    internal const string ManifestFileName = "_manifest.v1.json";
    internal const string SchemaVersion = "grandumi.card-content-manifest.v1";
    internal const string PlayabilityFileName = "_playability.v1.json";
    internal const string PlayabilitySchemaVersion = "grandumi.card-playability.v1";
    internal const string PendingReason = "effect-implementation-pending";

    public static ValidatedCardContentManifest Validate(string cardDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardDataRoot);
        var root = Path.GetFullPath(cardDataRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"卡牌数据目录不存在：{root}");

        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"卡牌数据缺少权威清单：{manifestPath}");
        ManifestDocument manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ManifestDocument>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("卡牌数据清单为空");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"卡牌数据清单 JSON 无效：{exception.Message}", exception);
        }

        if (!string.Equals(manifest.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"卡牌数据清单版本无效：{manifest.SchemaVersion ?? "<空>"}");
        if (manifest.Schema is null || manifest.Files is null || manifest.Files.Count == 0)
            throw new InvalidDataException("卡牌数据清单缺少 schema 或卡集文件");
        ValidateHash(manifest.Schema.Sha256, "schema.sha256");
        ValidateHash(manifest.ContentSha256, "contentSha256");

        var schemaPath = ResolveDirectFile(root, manifest.Schema.Path, "schema");
        var actualSchemaHash = HashFile(schemaPath);
        if (!string.Equals(actualSchemaHash, manifest.Schema.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("卡牌 schema 文件哈希与清单不一致");

        var declared = manifest.Files.Select(entry => entry.Path).ToArray();
        if (declared.Any(string.IsNullOrWhiteSpace)
            || declared.Distinct(StringComparer.Ordinal).Count() != declared.Length)
            throw new InvalidDataException("卡牌数据清单含空路径或重复路径");
        var sorted = declared.Order(StringComparer.Ordinal).ToArray();
        if (!declared.SequenceEqual(sorted, StringComparer.Ordinal))
            throw new InvalidDataException("卡牌数据清单必须按路径进行 Ordinal 排序");

        var actualFiles = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith("_", StringComparison.Ordinal))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!declared.SequenceEqual(actualFiles, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"卡牌数据文件集合与清单不一致；声明 [{string.Join(',', declared)}]，实际 [{string.Join(',', actualFiles)}]");

        var content = new StringBuilder();
        var totalCards = 0;
        var cardNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            ValidateHash(entry.Sha256, $"{entry.Path}.sha256");
            if (entry.CardCount < 0) throw new InvalidDataException($"{entry.Path} 的 cardCount 不能为负数");
            var filePath = ResolveDirectFile(root, entry.Path, "card set");
            var actualHash = HashFile(filePath);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"卡牌数据文件哈希与清单不一致：{entry.Path}");
            int actualCount;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(filePath));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"卡牌数据根节点必须是数组：{entry.Path}");
                actualCount = document.RootElement.GetArrayLength();
                foreach (var card in document.RootElement.EnumerateArray())
                {
                    if (!card.TryGetProperty("number", out var numberElement)
                        || numberElement.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(numberElement.GetString()))
                        throw new InvalidDataException($"卡牌数据缺少有效卡号：{entry.Path}");
                    var number = numberElement.GetString()!;
                    if (!cardNumbers.Add(number))
                        throw new InvalidDataException($"卡牌番号重复：{number}");
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"卡牌数据 JSON 无效：{entry.Path}：{exception.Message}", exception);
            }
            if (actualCount != entry.CardCount)
                throw new InvalidDataException($"卡牌数量与清单不一致：{entry.Path} 声明 {entry.CardCount}，实际 {actualCount}");
            totalCards += actualCount;
            content.Append(entry.Path).Append('\0').Append(entry.Sha256).Append('\0').Append(entry.CardCount).Append('\n');
        }

        if (totalCards != manifest.TotalCards)
            throw new InvalidDataException($"卡牌总数与清单不一致：声明 {manifest.TotalCards}，实际 {totalCards}");
        var contentHash = HashBytes(Encoding.UTF8.GetBytes(content.ToString()));
        if (!string.Equals(contentHash, manifest.ContentSha256, StringComparison.Ordinal))
            throw new InvalidDataException("卡牌数据 contentSha256 无效");

        var (playabilityFile, pendingCards) = ValidatePlayability(root, manifest.Playability, cardNumbers);
        return new ValidatedCardContentManifest(declared, totalCards, contentHash, playabilityFile, pendingCards);
    }

    private static (string? File, IReadOnlySet<string> PendingCards) ValidatePlayability(
        string root,
        ManifestPlayability? declaration,
        IReadOnlySet<string> cardNumbers)
    {
        if (declaration is null)
        {
            if (File.Exists(Path.Combine(root, PlayabilityFileName)))
                throw new InvalidDataException("卡牌数据存在未被主清单声明的可用状态文件");
            return (null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        ValidateHash(declaration.Sha256, "playability.sha256");
        if (declaration.PendingCardCount < 0)
            throw new InvalidDataException("卡牌可用状态清单 pendingCardCount 不能为负数");
        var filePath = ResolveDirectFile(root, declaration.Path, "playability");
        if (!string.Equals(HashFile(filePath), declaration.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("卡牌可用状态文件哈希与清单不一致");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(filePath));
            var node = document.RootElement;
            if (node.ValueKind != JsonValueKind.Object
                || !node.TryGetProperty("schemaVersion", out var schema)
                || schema.GetString() != PlayabilitySchemaVersion)
                throw new InvalidDataException("卡牌可用状态清单 schemaVersion 无效");
            if (!node.TryGetProperty("pendingReason", out var reason)
                || reason.GetString() != PendingReason)
                throw new InvalidDataException("卡牌可用状态清单 pendingReason 无效");
            if (!node.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("卡牌可用状态清单 cards 必须是数组");

            var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? previous = null;
            foreach (var entry in cards.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || entry.EnumerateObject().Select(property => property.Name)
                        .Order(StringComparer.Ordinal).SequenceEqual(["number", "state"], StringComparer.Ordinal) is false
                    || !entry.TryGetProperty("number", out var numberElement)
                    || numberElement.ValueKind != JsonValueKind.String
                    || !entry.TryGetProperty("state", out var stateElement)
                    || stateElement.GetString() != "pending")
                    throw new InvalidDataException("卡牌可用状态条目只允许有效的 number 与 pending state");

                var number = numberElement.GetString()!;
                if (!cardNumbers.Contains(number))
                    throw new InvalidDataException($"卡牌可用状态引用未知卡号：{number}");
                if (previous is not null && string.CompareOrdinal(previous, number) >= 0)
                    throw new InvalidDataException("卡牌可用状态条目必须按卡号严格升序排列且不得重复");
                if (!pending.Add(number))
                    throw new InvalidDataException($"卡牌可用状态含重复卡号：{number}");
                previous = number;
            }
            if (pending.Count != declaration.PendingCardCount)
                throw new InvalidDataException(
                    $"卡牌可用状态数量与清单不一致：声明 {declaration.PendingCardCount}，实际 {pending.Count}");
            return (declaration.Path, pending);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"卡牌可用状态 JSON 无效：{exception.Message}", exception);
        }
    }

    private static string ResolveDirectFile(string root, string? relativePath, string label)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || !string.Equals(Path.GetFileName(relativePath), relativePath, StringComparison.Ordinal)
            || relativePath.Contains(Path.DirectorySeparatorChar)
            || relativePath.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidDataException($"卡牌数据清单 {label} 路径越界或格式无效：{relativePath ?? "<空>"}");
        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath)) throw new InvalidDataException($"卡牌数据清单引用的文件不存在：{relativePath}");
        return fullPath;
    }

    private static void ValidateHash(string? value, string label)
    {
        if (value is null || value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            throw new InvalidDataException($"卡牌数据清单 {label} 必须是 64 位小写 SHA-256");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ManifestDocument
    {
        [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; set; }
        [JsonPropertyName("schema")] public ManifestSchema? Schema { get; set; }
        [JsonPropertyName("totalCards")] public int TotalCards { get; set; }
        [JsonPropertyName("contentSha256")] public string? ContentSha256 { get; set; }
        [JsonPropertyName("files")] public List<ManifestEntry>? Files { get; set; }
        [JsonPropertyName("playability")] public ManifestPlayability? Playability { get; set; }
    }

    private sealed class ManifestSchema
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("cardCount")] public int CardCount { get; set; }
    }

    private sealed class ManifestPlayability
    {
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("pendingCardCount")] public int PendingCardCount { get; set; }
    }
}
