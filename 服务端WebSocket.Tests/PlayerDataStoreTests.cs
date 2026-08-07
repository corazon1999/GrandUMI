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
}
