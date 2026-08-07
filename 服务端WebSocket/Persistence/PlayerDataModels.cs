using System.Text.Json.Serialization;

namespace GrandUMI.Persistence;

/// <summary>云端卡组的持久化快照。</summary>
public sealed record StoredDeck
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("leader")]
    public string Leader { get; init; } = "";

    [JsonPropertyName("leaderName")]
    public string LeaderName { get; init; } = "";

    [JsonPropertyName("leaderSprite")]
    public string LeaderSprite { get; init; } = "";

    [JsonPropertyName("charCount")]
    public int CharCount { get; init; }

    [JsonPropertyName("eventCount")]
    public int EventCount { get; init; }

    [JsonPropertyName("stageCount")]
    public int StageCount { get; init; }

    [JsonPropertyName("cards")]
    public string[] Cards { get; init; } = [];

    [JsonPropertyName("spriteMap")]
    public Dictionary<string, string> SpriteMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; init; }
}

/// <summary>服务端返回的玩家全量云端数据。</summary>
public sealed record PlayerDataSnapshot(
    string Account,
    string DisplayName,
    string Avatar,
    string CardBackId,
    string? SelectedDeckName,
    IReadOnlyList<StoredDeck> Decks);

public sealed record DeckImportResult(PlayerDataSnapshot Snapshot, int Imported, int Renamed, int Skipped);

public sealed class PlayerDataValidationException(string message) : Exception(message);
