using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderChampionStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "grandumi-leader-champion-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void 满足二十场后按保守胜率选出唯一最强使用者()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames - 1; index++)
        {
            // 完成第 20 场后，Alice 为 16 胜 4 负，Bob 为 14 胜 6 负。
            store.RecordMatch(Match($"alice-{index}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", index < 16 ? 0 : 1));
            store.RecordMatch(Match($"bob-{index}", now, "Bob", "Opponent-B", "OP16-001", "OP01-001", index < 14 ? 0 : 1));
        }

        Assert.Null(store.GetChampion("OP16-001", now));
        var lastIndex = LeaderChampionStore.MinimumChampionGames - 1;
        store.RecordMatch(Match($"alice-{lastIndex}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", 1));
        store.RecordMatch(Match($"bob-{lastIndex}", now, "Bob", "Opponent-B", "OP16-001", "OP01-001", 1));

        Assert.True(store.IsChampion("alice", "OP16-001", now));
        Assert.False(store.IsChampion("Bob", "OP16-001", now));
        Assert.Equal(new[] { "OP16-001" }, store.GetChampionLeaderNumbers("ALICE", now));
    }

    [Fact]
    public void 私人对局掉线和不足场次不会授予称号()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames; index++)
        {
            Assert.False(store.RecordMatch(Match($"friendly-{index}", now, "Alice", "Bob", "OP16-001", "OP01-001", 0, MatchKind.Friendly)));
        }
        Assert.False(store.RecordMatch(Match("disconnect", now, "Alice", "Bob", "OP16-001", "OP01-001", 0, MatchKind.Ranked, "对手断线")));

        Assert.Empty(store.GetChampionLeaderNumbers("Alice", now));
    }

    [Fact]
    public void 同分时以场次再以账号哈希稳定决出唯一持有者()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames; index++)
        {
            store.RecordMatch(Match($"alice-{index}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", 0));
        }
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames + 1; index++)
        {
            store.RecordMatch(Match($"bob-{index}", now, "Bob", "Opponent-B", "OP16-001", "OP01-001", 0));
        }

        Assert.True(store.IsChampion("Bob", "OP16-001", now));
        Assert.False(store.IsChampion("Alice", "OP16-001", now));
    }

    [Fact]
    public void 多个Leader会分别加载各自的最强使用者()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames; index++)
        {
            store.RecordMatch(Match($"ace-{index}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", 0));
            store.RecordMatch(Match($"kid-{index}", now, "Bob", "Opponent-B", "OP17-020", "OP02-001", 0));
            store.RecordMatch(Match($"bonney-{index}", now, "Carol", "Opponent-C", "OP17-039", "OP03-001", 0));
        }

        Assert.Equal("OP16-001", store.GetChampion("OP16-001", now)?.LeaderNumber);
        Assert.Equal("OP17-020", store.GetChampion("OP17-020", now)?.LeaderNumber);
        Assert.Equal("OP17-039", store.GetChampion("OP17-039", now)?.LeaderNumber);
        Assert.True(store.IsChampion("Alice", "OP16-001", now));
        Assert.True(store.IsChampion("Bob", "OP17-020", now));
        Assert.True(store.IsChampion("Carol", "OP17-039", now));
    }

    [Fact]
    public void 测试服从独立排行榜数据库读取最强使用者()
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var writePath = Path.Combine(_tempDir, "test-server.db");
        var leaderboardPath = Path.Combine(_tempDir, "production-leaderboard.db");
        var leaderboardStore = new LeaderChampionStore(leaderboardPath);
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames; index++)
        {
            leaderboardStore.RecordMatch(Match(
                $"production-{index}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", 0));
        }

        var testServerStore = new LeaderChampionStore(writePath, leaderboardPath);
        testServerStore.Initialize();

        Assert.Equal(Path.GetFullPath(writePath), testServerStore.DatabasePath);
        Assert.Equal(Path.GetFullPath(leaderboardPath), testServerStore.LeaderboardDatabasePath);
        Assert.True(testServerStore.IsChampion("Alice", "OP16-001", now));
    }

    [Theory]
    [InlineData(MatchKind.Ranked)]
    [InlineData(MatchKind.RankedWild)]
    [InlineData(MatchKind.Casual)]
    [InlineData(MatchKind.CasualStandard)]
    [InlineData(MatchKind.CasualWild)]
    [InlineData(MatchKind.Matchmaking)]
    public void 初始化时会从各种公开匹配历史回填最强使用者(MatchKind matchKind)
    {
        var now = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var databasePath = Path.Combine(_tempDir, "leader-stats.db");
        var statsStore = new LeaderStatsStore(databasePath);
        for (var index = 0; index < LeaderChampionStore.MinimumChampionGames; index++)
        {
            Assert.True(statsStore.RecordMatch(Match(
                $"history-{index}", now, "Alice", "Opponent-A", "OP16-001", "OP01-001", 0, matchKind)));
        }

        var championStore = new LeaderChampionStore(databasePath);
        championStore.Initialize();

        Assert.True(championStore.IsChampion("Alice", "OP16-001", now));
    }

    private LeaderChampionStore CreateStore()
    {
        Directory.CreateDirectory(_tempDir);
        return new LeaderChampionStore(Path.Combine(_tempDir, "champions.db"));
    }

    private static LeaderMatchResult Match(
        string id,
        DateTime at,
        string player0,
        string player1,
        string leader0,
        string leader1,
        int winner,
        MatchKind kind = MatchKind.Ranked,
        string reason = "胜利")
        => new(id, at, kind, player0, player1, leader0, leader1, winner, 0, 8, reason);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
