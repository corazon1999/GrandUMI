using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

public sealed class PlayerDataStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "grandumi-player-data-tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public PlayerDataStoreTests()
    {
        _databasePath = Path.Combine(_tempDir, "players.db");
    }

    [Fact]
    public void Login_CreatesPlayer_AndPersistsAcrossStoreInstances()
    {
        var firstStore = CreateStore();
        var first = firstStore.Login("  Alice  ");

        Assert.Equal("Alice", first.Account);
        Assert.Equal("Alice", first.DisplayName);
        Assert.Equal(PlayerDataStore.DefaultCardBackId, first.CardBackId);
        Assert.Empty(first.Decks);

        var restartedStore = CreateStore();
        var afterRestart = restartedStore.Login("alice");

        Assert.Equal("Alice", afterRestart.Account);
        Assert.Equal("Alice", afterRestart.DisplayName);
    }

    [Fact]
    public void DeckCrud_IsIsolatedByAccount_AndDeleteClearsSelection()
    {
        var store = CreateStore();
        store.Login("玩家甲");
        store.Login("玩家乙");

        var saved = store.SaveDeck("玩家甲", Deck("红色卡组"));
        Assert.Single(saved.Decks);

        var selected = store.SelectDeck("玩家甲", "红色卡组");
        Assert.Equal("红色卡组", selected.SelectedDeckName);

        var otherPlayer = store.Login("玩家乙");
        Assert.Empty(otherPlayer.Decks);

        var deleted = store.DeleteDeck("玩家甲", "红色卡组");
        Assert.Empty(deleted.Decks);
        Assert.Null(deleted.SelectedDeckName);
    }

    [Fact]
    public void ImportDecks_RenamesDifferentConflicts_AndSkipsIdenticalOnes()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.SaveDeck("Alice", Deck("我的卡组", "OP15-003"));

        var result = store.ImportDecks("Alice", [
            Deck("我的卡组", "OP15-003"),
            Deck("我的卡组", "OP15-004"),
        ]);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Renamed);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Snapshot.Decks, deck => deck.Name == "我的卡组（本地导入）");
    }

    [Fact]
    public void Validation_RejectsInvalidDeckAndExternalAvatar()
    {
        var store = CreateStore();
        store.Login("Alice");

        var invalidDeck = Deck("少牌") with { Cards = ["OP15-003"] };
        Assert.Throws<PlayerDataValidationException>(() => store.SaveDeck("Alice", invalidDeck));
        Assert.Throws<PlayerDataValidationException>(() => store.UpdateProfile("Alice", "Alice", "https://example.com/avatar.png"));
    }

    [Fact]
    public void UpdateProfile_PersistsDisplayNameAndAvatar()
    {
        var store = CreateStore();
        store.Login("Alice");

        var updated = store.UpdateProfile("Alice", "航海士", "/sprites-thumb/OP15/OP15-001.webp");
        var reloaded = store.Login("ALICE");

        Assert.Equal("航海士", updated.DisplayName);
        Assert.Equal("航海士", reloaded.DisplayName);
        Assert.Equal("/sprites-thumb/OP15/OP15-001.webp", reloaded.Avatar);
    }

    [Fact]
    public void UpdateCardBack_只接受内置卡背并跨登录持久化()
    {
        var store = CreateStore();
        store.Login("Alice");

        var updated = store.UpdateCardBack("Alice", "straw-hat").Snapshot;
        var reloaded = store.Login("alice");

        Assert.Equal("straw-hat", updated.CardBackId);
        Assert.Equal("straw-hat", reloaded.CardBackId);
        Assert.Throws<PlayerDataValidationException>(() => store.UpdateCardBack("Alice", "https://example.com/back.png"));
    }

    [Fact]
    public void CardBackGallery_上传命名点赞排序与选用自动点赞形成闭环()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");

        store.UploadCardBack("Alice", "海上日出", "image/png", TinyPngBase64());
        store.UploadCardBack("Bob", "月下航路", "image/png", TinyPngBase64());
        var before = store.GetCardBackGallery("Alice");
        var aliceBack = Assert.Single(before, item => item.Name == "海上日出");
        var bobBack = Assert.Single(before, item => item.Name == "月下航路");
        Assert.True(aliceBack.Owned);
        Assert.False(bobBack.Liked);

        var selected = store.UpdateCardBack("Alice", bobBack.Id);
        Assert.Equal(bobBack.Id, selected.Snapshot.CardBackId);
        var selectedItem = Assert.Single(selected.Gallery, item => item.Id == bobBack.Id);
        Assert.True(selectedItem.Liked);
        Assert.Equal(1, selectedItem.Likes);

        store.ToggleCardBackLike("Bob", bobBack.Id);
        var ranked = store.GetCardBackGallery("Alice");
        Assert.Equal(bobBack.Id, ranked[0].Id);
        Assert.Equal(2, ranked[0].Likes);
        Assert.Equal(bobBack.Id, store.Login("alice").CardBackId);

        var numericId = long.Parse(bobBack.Id["custom-".Length..]);
        var image = store.GetCardBackImage(numericId);
        Assert.NotNull(image);
        Assert.Equal("image/png", image.MimeType);
        Assert.NotEmpty(image.Data);
    }

    [Fact]
    public void CardBackGallery_拒绝重名伪造类型过大图片与不存在卡背()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.UploadCardBack("Alice", "远航", "image/png", TinyPngBase64());

        Assert.Throws<PlayerDataValidationException>(() => store.UploadCardBack("Alice", "远航", "image/png", TinyPngBase64()));
        Assert.Throws<PlayerDataValidationException>(() => store.UploadCardBack("Alice", "伪图", "image/jpeg", TinyPngBase64()));
        Assert.Throws<PlayerDataValidationException>(() => store.UploadCardBack(
            "Alice", "过大", "image/png", Convert.ToBase64String(new byte[PlayerDataStore.MaxCardBackImageBytes + 1])));
        Assert.Throws<PlayerDataValidationException>(() => store.UpdateCardBack("Alice", "custom-999999"));
    }

    [Fact]
    public void CardBackGallery_只有发布者可删除且删除后重置所有选用者()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");
        store.UploadCardBack("Alice", "待删除卡背", "image/png", TinyPngBase64());
        var cardBack = Assert.Single(store.GetCardBackGallery("Alice"));
        store.UpdateCardBack("Alice", cardBack.Id);
        store.UpdateCardBack("Bob", cardBack.Id);

        Assert.Throws<PlayerDataValidationException>(() => store.DeleteCardBack("Bob", cardBack.Id));

        var deleted = store.DeleteCardBack("Alice", cardBack.Id);
        Assert.Equal(cardBack.Id, deleted.DeletedCardBackId);
        Assert.Equal(PlayerDataStore.DefaultCardBackId, deleted.Snapshot.CardBackId);
        Assert.Empty(deleted.Gallery);
        Assert.Equal(PlayerDataStore.DefaultCardBackId, store.GetPlayerData("Bob").CardBackId);
        Assert.Null(store.GetCardBackImage(long.Parse(cardBack.Id["custom-".Length..])));
        Assert.Throws<PlayerDataValidationException>(() => store.DeleteCardBack("Alice", cardBack.Id));
    }

    [Fact]
    public void DeferredLoginWrites_同一玩家重复登录只保留一次待写并在关服排空()
    {
        var store = new PlayerDataStore(_databasePath, deferLoginWrites: true);
        store.Initialize();
        store.Login("Alice");

        store.Login("alice");
        store.Login("ALICE");

        Assert.Equal(1, store.PendingLoginWrites);
        store.Shutdown();
        Assert.Equal(0, store.PendingLoginWrites);
    }

    [Fact]
    public void FriendRequest_Accept_Remove_完整关系持久化闭环()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");

        var sent = store.SendFriendRequest("Alice", "bob");
        Assert.Single(sent.Snapshot.OutgoingRequests);
        Assert.Equal("Bob", sent.OtherAccount);

        var bobBefore = store.GetFriendData("Bob");
        var request = Assert.Single(bobBefore.IncomingRequests);
        Assert.Equal("Alice", request.Account);

        var accepted = store.RespondFriendRequest("Bob", request.Id, accept: true);
        Assert.Single(accepted.Snapshot.Friends);
        Assert.Empty(accepted.Snapshot.IncomingRequests);
        Assert.Equal("Alice", accepted.OtherAccount);

        var afterRestart = CreateStore().GetFriendData("Alice");
        Assert.Equal("Bob", Assert.Single(afterRestart.Friends).Account);

        var removed = store.RemoveFriend("Alice", "BOB");
        Assert.Empty(removed.Snapshot.Friends);
        Assert.Empty(store.GetFriendData("Bob").Friends);
    }

    [Fact]
    public void FriendRequest_反向申请会自动互加且阻止重复与自己添加()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");
        store.SendFriendRequest("Alice", "Bob");

        var autoAccepted = store.SendFriendRequest("Bob", "Alice");

        Assert.True(autoAccepted.AutoAccepted);
        Assert.Equal("Alice", Assert.Single(autoAccepted.Snapshot.Friends).Account);
        Assert.Empty(autoAccepted.Snapshot.IncomingRequests);
        Assert.Empty(autoAccepted.Snapshot.OutgoingRequests);
        Assert.Throws<PlayerDataValidationException>(() => store.SendFriendRequest("Alice", "Bob"));
        Assert.Throws<PlayerDataValidationException>(() => store.SendFriendRequest("Alice", "alice"));
        Assert.Throws<PlayerDataValidationException>(() => store.SendFriendRequest("Alice", "Nobody"));
    }

    [Fact]
    public void FriendRequest_拒绝后双方申请列表清空()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");
        store.SendFriendRequest("Alice", "Bob");
        var request = Assert.Single(store.GetFriendData("Bob").IncomingRequests);

        store.RespondFriendRequest("Bob", request.Id, accept: false);

        Assert.Empty(store.GetFriendData("Alice").OutgoingRequests);
        Assert.Empty(store.GetFriendData("Bob").IncomingRequests);
        Assert.Empty(store.GetFriendData("Alice").Friends);
    }

    [Fact]
    public void FriendRequest_发送方可以撤回申请但接收方不能冒充撤回()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");
        var sent = store.SendFriendRequest("Alice", "Bob");
        var request = Assert.Single(sent.Snapshot.OutgoingRequests);

        Assert.Throws<PlayerDataValidationException>(() => store.CancelFriendRequest("Bob", request.Id));
        store.CancelFriendRequest("Alice", request.Id);

        Assert.Empty(store.GetFriendData("Alice").OutgoingRequests);
        Assert.Empty(store.GetFriendData("Bob").IncomingRequests);
    }

    [Fact]
    public void SearchPlayers_返回账号昵称匹配及当前好友关系()
    {
        var store = CreateStore();
        store.Login("Alice");
        store.Login("Bob");
        store.Login("Bobby");
        store.UpdateProfile("Bob", "航海士波波", "");
        store.SendFriendRequest("Alice", "Bob");

        var byName = store.SearchPlayers("Alice", "航海士");
        var bob = Assert.Single(byName);
        Assert.Equal("Bob", bob.Account);
        Assert.Equal("outgoing", bob.Relationship);

        var byAccount = store.SearchPlayers("Alice", "bobb");
        Assert.Equal("Bobby", Assert.Single(byAccount).Account);
        Assert.DoesNotContain(store.SearchPlayers("Alice", "Alice"), player => player.Account == "Alice");
    }

    private PlayerDataStore CreateStore()
    {
        var store = new PlayerDataStore(_databasePath);
        store.Initialize();
        return store;
    }

    private static StoredDeck Deck(string name, string cardNumber = "OP15-003")
        => new()
        {
            Name = name,
            Leader = "OP15-001",
            LeaderName = "红发杰克",
            LeaderSprite = "/sprites-thumb/OP15/OP15-001.webp",
            CharCount = 50,
            Cards = Enumerable.Repeat(cardNumber, 50).ToArray(),
            SpriteMap = new Dictionary<string, string>(),
            UpdatedAt = 1_700_000_000_000,
        };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static string TinyPngBase64()
    {
        byte[] bytes = [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        ];
        return Convert.ToBase64String(bytes);
    }
}
