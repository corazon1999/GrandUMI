using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

public sealed class PlayerSafetyPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PlayerDataStore _store;

    public PlayerSafetyPersistenceTests()
    {
        var root = OperatingSystem.IsWindows()
            ? @"E:\GrandUMI-Temp\server-tests"
            : "/tmp/grandumi-server-tests";
        _tempDir = Path.Combine(root, $"player-safety-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new PlayerDataStore(Path.Combine(_tempDir, "players.db"));
        _store.Initialize();
        _store.Login("Alice");
        _store.Login("Bob");
    }

    [Fact]
    public void 屏蔽玩家会解除好友关系并阻止消息申请与搜索互见()
    {
        _store.SendFriendRequest("Alice", "Bob");
        _store.SendFriendRequest("Bob", "Alice");
        Assert.True(_store.AreFriends("Alice", "Bob"));

        _store.BlockPlayer("Alice", "Bob");

        Assert.False(_store.AreFriends("Alice", "Bob"));
        Assert.Equal("Bob", Assert.Single(_store.GetBlockedPlayers("Alice")).Account);
        Assert.Contains("bob", _store.GetBlockedRelatedAccountKeys("Alice"));
        Assert.Throws<PlayerDataValidationException>(() => _store.SendFriendRequest("Bob", "Alice"));
        Assert.Throws<PlayerDataValidationException>(() =>
            _store.QueueFriendMessage("Alice", "Bob", "blocked-message", "骚扰信息", 1));
        Assert.Empty(_store.SearchPlayers("Alice", "Bob"));
    }

    [Fact]
    public void 解除屏蔽后可重新申请好友且举报会持久化()
    {
        _store.BlockPlayer("Alice", "Bob");
        _store.UnblockPlayer("Alice", "Bob");

        Assert.Empty(_store.GetBlockedPlayers("Alice"));
        _store.SendFriendRequest("Alice", "Bob");
        _store.CreatePlayerReport("Alice", "Bob", "harassment", "持续发送侮辱性消息", "{\"roomId\":\"test\"}");
    }

    [Fact]
    public void 举报仅接受固定分类且拦截过短说明和短时间重复提交()
    {
        Assert.Throws<PlayerDataValidationException>(() =>
            _store.CreatePlayerReport("Alice", "Bob", "unknown", "不受支持的类别", "{}"));
        Assert.Throws<PlayerDataValidationException>(() =>
            _store.CreatePlayerReport("Alice", "Bob", "stalling", "慢", "{}"));

        _store.CreatePlayerReport(
            "Alice",
            "Bob",
            "stalling",
            "连续多个回合故意耗尽操作时间",
            "{\"roomId\":\"ranked-room\",\"turnCount\":8}");

        var duplicate = Assert.Throws<PlayerDataValidationException>(() =>
            _store.CreatePlayerReport(
                "Alice",
                "Bob",
                "stalling",
                "仍然在故意拖延",
                "{\"roomId\":\"ranked-room\",\"turnCount\":8}"));
        Assert.Contains("请勿重复提交", duplicate.Message);

        _store.CreatePlayerReport(
            "Alice",
            "Bob",
            "cheating",
            "疑似利用异常信息获利",
            "{\"roomId\":\"ranked-room\",\"turnCount\":8}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
