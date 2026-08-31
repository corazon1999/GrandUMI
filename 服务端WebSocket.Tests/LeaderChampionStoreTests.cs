using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderChampionStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_DIR") ?? Path.GetTempPath(),
        "grandumi-leader-champion-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void 动态门槛和贝叶斯参数使用固定可解释边界()
    {
        Assert.Equal(LeaderChampionStore.LowVolumeMinimumChampionGames,
            LeaderChampionStore.MinimumGamesForLeader(LeaderChampionStore.LowVolumeLeaderMatchThreshold - 1));
        Assert.Equal(LeaderChampionStore.DefaultMinimumChampionGames,
            LeaderChampionStore.MinimumGamesForLeader(LeaderChampionStore.LowVolumeLeaderMatchThreshold));

        Assert.Equal(0.5,
            LeaderChampionStore.BayesianAdjustedWinRate(15, 30, 100, 200), 12);
        Assert.Equal(0.8,
            LeaderChampionStore.BayesianAdjustedWinRate(30, 30, 0, 0), 12);
        Assert.InRange(
            LeaderChampionStore.BayesianAdjustedWinRate(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            0,
            1);
    }

    [Fact]
    public void 低场次Leader达到三十场后才产生唯一最强使用者()
    {
        var now = UtcNow();
        var store = CreateStore();
        RecordCandidate(store, now, "alice", "OP16-001", 0, 29, 22);

        Assert.Null(store.GetChampion("OP16-001", now));

        RecordCandidate(store, now, "alice", "OP16-001", 29, 1, 0);

        Assert.True(store.IsChampion("ALICE", "OP16-001", now));
        Assert.Equal(new[] { "OP16-001" }, store.GetChampionLeaderNumbers("alice", now));
    }

    [Fact]
    public void Leader总场次达到一千局时恢复五十场门槛()
    {
        var now = UtcNow();
        var store = CreateStore();
        RecordCandidate(store, now, "alice", "OP16-001", 0, 30, 24);
        RecordFillerMatches(store, now, "OP16-001", 0, 969);

        Assert.Equal("alice", ChampionAccount(store, "OP16-001", now));

        RecordFillerMatches(store, now, "OP16-001", 969, 1);
        Assert.Null(store.GetChampion("OP16-001", now));

        RecordCandidate(store, now, "alice", "OP16-001", 30, 20, 14);
        Assert.Equal(50, store.GetChampion("OP16-001", now)?.Games);
    }

    [Fact]
    public void 活跃日按UTC加八自然日计算且必须面对十五名匿名对手()
    {
        var now = UtcNow();
        var store = CreateStore();
        var fiveBusinessDaysAcrossFourUtcDates = new[]
        {
            new DateTime(2026, 8, 10, 15, 59, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 10, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 11, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 16, 0, 0, DateTimeKind.Utc),
        };
        RecordCandidate(store, now, "alice", "OP16-001", 0, 30, 20,
            activeTimes: fiveBusinessDaysAcrossFourUtcDates);
        RecordCandidate(store, now, "bob", "OP17-020", 0, 30, 20, activeDays: 4);
        RecordCandidate(store, now, "carol", "OP17-039", 0, 30, 20, opponentCount: 14);

        Assert.True(store.IsChampion("alice", "OP16-001", now));
        Assert.Null(store.GetChampion("OP17-020", now));
        Assert.Null(store.GetChampion("OP17-039", now));
    }

    [Fact]
    public void 贝叶斯修正会降低小样本连胜优势()
    {
        var now = UtcNow();
        var store = CreateStore();
        RecordCandidate(store, now, "alice", "OP16-001", 0, 30, 27);
        RecordCandidate(store, now, "bob", "OP16-001", 0, 50, 43);
        RecordBalancedPopulation(store, now, "OP16-001", 200);

        var champion = store.GetChampion("OP16-001", now);

        Assert.NotNull(champion);
        Assert.Equal("bob", ChampionAccount(store, "OP16-001", now));
        Assert.Equal(50, champion!.Games);
        Assert.True(27 / 30d > 43 / 50d);
    }

    [Fact]
    public void 镜像局总场次只计一局且候选整局不进入自身先验()
    {
        var now = UtcNow();
        var store = CreateStore();
        for (var index = 0; index < LeaderChampionStore.LowVolumeMinimumChampionGames; index++)
        {
            Assert.True(store.RecordMatch(Match(
                $"mirror-{index}",
                now.AddDays(-(index % LeaderChampionStore.MinimumActiveDays)),
                "alice",
                $"mirror-opponent-{index}",
                "OP16-001",
                "OP16-001",
                0)));
        }

        var champion = store.GetChampion("OP16-001", now);

        Assert.NotNull(champion);
        Assert.Equal(30, champion!.Games);
        Assert.Equal(0.8, champion.Score, 12);
    }

    [Fact]
    public void 同评分时按场次胜场和匿名键稳定选出唯一持有者()
    {
        var candidates = new[]
        {
            new LeaderChampion("OP16-001", "c", 40, 30, 0.75),
            new LeaderChampion("OP16-001", "b", 50, 29, 0.75),
            new LeaderChampion("OP16-001", "a", 50, 31, 0.75),
        };

        Assert.Equal("a", LeaderChampionStore.SelectChampion(candidates).PlayerKey);
        Assert.Equal("a", LeaderChampionStore.SelectChampion(candidates.Reverse()).PlayerKey);
    }

    [Fact]
    public void 重复对局无效公开局窗口外和未来对局不会影响称号()
    {
        var now = UtcNow();
        var store = CreateStore();
        Assert.False(store.RecordMatch(Match("friendly", now, "alice", "bob", "OP16-001", "OP01-001", 0, MatchKind.Friendly)));
        Assert.False(store.RecordMatch(Match("disconnect", now, "alice", "bob", "OP16-001", "OP01-001", 0, MatchKind.Ranked, "对手断线")));
        Assert.False(store.RecordMatch(Match("short", now, "alice", "bob", "OP16-001", "OP01-001", 0, turnCount: 7)));

        RecordCandidate(store, now, "alice", "OP16-001", 0, 29, 22);
        var duplicate = Match("alice-0", now, "alice", "alice-opponent-0", "OP16-001", "OP01-001", 0);
        Assert.False(store.RecordMatch(duplicate));
        Assert.Null(store.GetChampion("OP16-001", now));

        RecordCandidate(store, now, "old", "OP17-020", 0, 30, 30, timeOffsetDays: -31);
        RecordCandidate(store, now, "future", "OP17-039", 0, 30, 30, timeOffsetDays: 10);
        Assert.Null(store.GetChampion("OP17-020", now));
        Assert.Null(store.GetChampion("OP17-039", now));
    }

    [Fact]
    public void 多个Leader会分别加载各自的最强使用者()
    {
        var now = UtcNow();
        var store = CreateStore();
        RecordCandidate(store, now, "alice", "OP16-001", 0, 30, 24);
        RecordCandidate(store, now, "bob", "OP17-020", 0, 30, 23);
        RecordCandidate(store, now, "carol", "OP17-039", 0, 30, 22);

        Assert.True(store.IsChampion("alice", "OP16-001", now));
        Assert.True(store.IsChampion("bob", "OP17-020", now));
        Assert.True(store.IsChampion("carol", "OP17-039", now));
    }

    [Fact]
    public void 装备称号不受当前使用Leader限制且资格失效时安全回退()
    {
        var now = UtcNow();
        var store = CreateStore();
        RecordCandidate(store, now, "alice", "OP16-001", 0, 30, 24);
        RecordCandidate(store, now, "alice", "OP17-020", 100, 30, 23);

        var owned = store.GetChampionLeaderNumbers("alice", now);
        store.RememberEquippedChampionLeaderNumber("alice", "OP17-020");

        Assert.Equal(2, owned.Count);
        Assert.Equal("OP17-020", store.ResolveEquippedChampionLeaderNumber("alice", now));

        store.RememberEquippedChampionLeaderNumber("alice", "OP99-999");
        Assert.Equal(owned[0], store.ResolveEquippedChampionLeaderNumber("alice", now));

        store.RememberEquippedChampionLeaderNumber("alice", null);
        Assert.Equal(owned[0], store.ResolveEquippedChampionLeaderNumber("alice", now));
        Assert.Null(store.ResolveEquippedChampionLeaderNumber("nobody", now));
    }

    [Fact]
    public void 测试服从独立排行榜数据库读取最强使用者()
    {
        var now = UtcNow();
        Directory.CreateDirectory(_tempDir);
        var writePath = Path.Combine(_tempDir, "test-server.db");
        var leaderboardPath = Path.Combine(_tempDir, "production-leaderboard.db");
        var leaderboardStore = new LeaderChampionStore(leaderboardPath);
        RecordCandidate(leaderboardStore, now, "alice", "OP16-001", 0, 30, 24);

        var testServerStore = new LeaderChampionStore(writePath, leaderboardPath);
        testServerStore.Initialize();

        Assert.Equal(Path.GetFullPath(writePath), testServerStore.DatabasePath);
        Assert.Equal(Path.GetFullPath(leaderboardPath), testServerStore.LeaderboardDatabasePath);
        Assert.True(testServerStore.IsChampion("alice", "OP16-001", now));
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
        var now = UtcNow();
        Directory.CreateDirectory(_tempDir);
        var databasePath = Path.Combine(_tempDir, $"leader-stats-{matchKind}.db");
        var statsStore = new LeaderStatsStore(databasePath);
        for (var index = 0; index < LeaderChampionStore.LowVolumeMinimumChampionGames; index++)
        {
            Assert.True(statsStore.RecordMatch(Match(
                $"history-{matchKind}-{index}",
                now.AddDays(-(index % LeaderChampionStore.MinimumActiveDays)),
                "alice",
                $"history-opponent-{index}",
                "OP16-001",
                "OP01-001",
                0,
                matchKind)));
        }

        var championStore = new LeaderChampionStore(databasePath);
        championStore.Initialize();

        Assert.True(championStore.IsChampion("alice", "OP16-001", now));
    }

    private LeaderChampionStore CreateStore()
    {
        Directory.CreateDirectory(_tempDir);
        return new LeaderChampionStore(Path.Combine(_tempDir, "champions.db"));
    }

    private static DateTime UtcNow() => new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private static void RecordCandidate(
        LeaderChampionStore store,
        DateTime now,
        string account,
        string leader,
        int startIndex,
        int count,
        int wins,
        int activeDays = LeaderChampionStore.MinimumActiveDays,
        int opponentCount = LeaderChampionStore.MinimumDistinctOpponents,
        IReadOnlyList<DateTime>? activeTimes = null,
        int timeOffsetDays = 0)
    {
        for (var offset = 0; offset < count; offset++)
        {
            var index = startIndex + offset;
            var endedAt = activeTimes is null
                ? now.AddDays(timeOffsetDays - (index % activeDays))
                : activeTimes[index % activeTimes.Count];
            Assert.True(store.RecordMatch(Match(
                $"{account}-{index}",
                endedAt,
                account,
                $"{account}-opponent-{index % opponentCount}",
                leader,
                "OP01-001",
                offset < wins ? 0 : 1)));
        }
    }

    private static void RecordFillerMatches(
        LeaderChampionStore store,
        DateTime now,
        string leader,
        int startIndex,
        int count)
    {
        for (var offset = 0; offset < count; offset++)
        {
            var index = startIndex + offset;
            Assert.True(store.RecordMatch(Match(
                $"filler-{leader}-{index}",
                now.AddDays(-(index % LeaderChampionStore.MinimumActiveDays)),
                $"filler-player-{index}",
                $"filler-opponent-{index}",
                leader,
                "OP01-001",
                index % 2)));
        }
    }

    private static void RecordBalancedPopulation(
        LeaderChampionStore store,
        DateTime now,
        string leader,
        int games)
    {
        for (var index = 0; index < games; index++)
        {
            Assert.True(store.RecordMatch(Match(
                $"population-{index}",
                now.AddDays(-(index % LeaderChampionStore.MinimumActiveDays)),
                $"population-player-{index}",
                $"population-opponent-{index}",
                leader,
                "OP01-001",
                index % 2)));
        }
    }

    private static string? ChampionAccount(LeaderChampionStore store, string leader, DateTime now)
    {
        foreach (var account in new[] { "alice", "bob", "carol" })
        {
            if (store.IsChampion(account, leader, now)) return account;
        }
        return null;
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
        string reason = "胜利",
        int turnCount = 8)
        => new(id, at, kind, player0, player1, leader0, leader1, winner, 0, turnCount, reason);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
