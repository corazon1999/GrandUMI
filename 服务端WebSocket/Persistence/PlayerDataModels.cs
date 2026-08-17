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
    bool CanChangeDisplayName,
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
    bool PubliclyListed,
    long CreatedAt,
    string ReviewStatus,
    string ReviewReason);

/// <summary>卡背广场游标分页；本人投稿单独返回，避免与热门列表分页互相干扰。</summary>
public sealed record CardBackGalleryPage(
    IReadOnlyList<CardBackGalleryItem> Items,
    IReadOnlyList<CardBackGalleryItem> OwnedItems,
    int PageSize,
    int Total,
    bool HasMore,
    string? NextCursor);

/// <summary>管理员卡背审核队列中的待处理投稿。</summary>
public sealed record CardBackReviewItem(
    string Id,
    string Name,
    string AuthorName,
    string ImageUrl,
    long CreatedAt);

public sealed record CardBackImage(string MimeType, byte[] Data);

public sealed record CardBackSelectionResult(
    PlayerDataSnapshot Snapshot,
    CardBackGalleryItem? GalleryItem);

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

/// <summary>等待投递或刚刚投递的好友消息。</summary>
public sealed record QueuedFriendMessage(
    string Id,
    string Text,
    string FromAccount,
    string FromName,
    string ToAccount,
    string ToName,
    long SentAt);

public sealed record BlockedPlayerSnapshot(
    string Account,
    string DisplayName,
    long BlockedAt);

public sealed class PlayerDataValidationException(string message) : Exception(message);
