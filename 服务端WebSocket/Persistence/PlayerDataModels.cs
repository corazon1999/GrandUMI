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

/// <summary>卡背广场中的玩家投稿（图片本体通过只读 HTTP 地址按需获取）。</summary>
public sealed record CardBackGalleryItem(
    string Id,
    string Name,
    string AuthorName,
    string ImageUrl,
    int Likes,
    bool Liked,
    bool Owned,
    long CreatedAt);

public sealed record CardBackImage(string MimeType, byte[] Data);

public sealed record CardBackSelectionResult(
    PlayerDataSnapshot Snapshot,
    IReadOnlyList<CardBackGalleryItem> Gallery);

public sealed record CardBackDeletionResult(
    string DeletedCardBackId,
    PlayerDataSnapshot Snapshot,
    IReadOnlyList<CardBackGalleryItem> Gallery);

public sealed record DeckImportResult(PlayerDataSnapshot Snapshot, int Imported, int Renamed, int Skipped);

/// <summary>卡组广场中的公开构筑快照。</summary>
public sealed record DeckPlazaItem(
    string Id,
    string Title,
    string AuthorName,
    string Leader,
    string LeaderName,
    string LeaderSprite,
    string LeaderColor,
    int CharCount,
    int EventCount,
    int StageCount,
    string[] Cards,
    Dictionary<string, string> SpriteMap,
    int Likes,
    bool Liked,
    bool Owned,
    int Copies,
    long CreatedAt,
    long UpdatedAt);

public sealed record DeckPlazaPage(
    IReadOnlyList<DeckPlazaItem> Items,
    int Page,
    int PageSize,
    int Total,
    bool HasMore);

public sealed record DeckPlazaCopyResult(PlayerDataSnapshot Snapshot, string DeckName);

/// <summary>好友列表中的持久化玩家资料。</summary>
public sealed record FriendProfile(
    long PlayerId,
    string Account,
    string DisplayName,
    string Avatar,
    long FriendsSince);

/// <summary>待处理好友申请。</summary>
public sealed record FriendRequestSnapshot(
    long Id,
    string Account,
    string DisplayName,
    string Avatar,
    long CreatedAt);

/// <summary>某个玩家的完整好友状态。</summary>
public sealed record FriendDataSnapshot(
    IReadOnlyList<FriendProfile> Friends,
    IReadOnlyList<FriendRequestSnapshot> IncomingRequests,
    IReadOnlyList<FriendRequestSnapshot> OutgoingRequests);

/// <summary>好友搜索结果及其与当前玩家的关系。</summary>
public sealed record FriendSearchPlayer(
    string Account,
    string DisplayName,
    string Avatar,
    string Relationship);

/// <summary>好友关系写操作结果。</summary>
public sealed record FriendMutationResult(
    FriendDataSnapshot Snapshot,
    string OtherAccount,
    bool AutoAccepted = false);

public sealed class PlayerDataValidationException(string message) : Exception(message);
