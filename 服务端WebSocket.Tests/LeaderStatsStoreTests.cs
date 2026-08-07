using GrandUMI.Game;
using GrandUMI.Game.Stats;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LeaderStatsStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "grandumi-leader-stats-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void 全部真人模式计入且七回合以内和机器人对局排除()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();

        store.RecordMatch(Match("matchmaking", now, MatchKind.Matchmaking, "L-A", "L-B", 0, 0, 8));
        store.RecordMatch(Match("room-code", now, MatchKind.RoomCode, "L-A", "L-C", 1, 1, 10));
        store.RecordMatch(Match("friendly", now, MatchKind.Friendly, "L-D", "L-A", 1, 0, 12));
        store.RecordMatch(Match("too-short", now, MatchKind.Friendly, "L-A", "L-E", 0, 0, 7));
        store.RecordMatch(Match("bot", now, MatchKind.Bot, "L-A", "L-F", 0, 0, 20));

        var result = store.GetLeaderboard("all", now);

        Assert.Equal(3, result.TotalMatches);
        var leaderA = Assert.Single(result.Items, x => x.LeaderNumber == "L-A");
        Assert.Equal(3, leaderA.Games);
        Assert.Equal(2, leaderA.Wins);
        Assert.Equal(1, leaderA.Losses);
        Assert.Equal(2d / 3d, leaderA.WinRate, precision: 8);
        Assert.Equal(0.5, leaderA.UsageRate, precision: 8);
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber is "L-E" or "L-F");
    }

    [Fact]
    public void 重复对局幂等且相同账号对局不参与榜单()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        var match = Match("same-id", now, MatchKind.RoomCode, "L-A", "L-B", 0, 1, 8);

        Assert.True(store.RecordMatch(match));
        Assert.False(store.RecordMatch(match));
        Assert.True(store.RecordMatch(new LeaderMatchResult(
            "same-account",
            now,
            MatchKind.Friendly,
            "Alice",
            "alice",
            "L-A",
            "L-C",
            0,
            0,
            20,
            "测试")));

        var result = store.GetLeaderboard("all", now);
        Assert.Equal(1, result.TotalMatches);
        Assert.All(result.Items, x => Assert.Equal(1, x.Games));
    }

    [Fact]
    public void 时间窗口和二十场排名门槛正确生效()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        for (var i = 0; i < LeaderStatsStore.MinimumRankedGames; i++)
        {
            store.RecordMatch(Match(
                $"recent-{i}",
                now.AddHours(-i),
                MatchKind.Matchmaking,
                "L-A",
                "L-B",
                0,
                i % 2,
                8));
        }
        store.RecordMatch(Match("old", now.AddDays(-8), MatchKind.Matchmaking, "L-C", "L-D", 0, 0, 8));

        var sevenDays = store.GetLeaderboard("7d", now);
        var all = store.GetLeaderboard("all", now);

        Assert.Equal(20, sevenDays.TotalMatches);
        Assert.Equal(1, Assert.Single(sevenDays.Items, x => x.LeaderNumber == "L-A").Rank);
        Assert.Equal(2, Assert.Single(sevenDays.Items, x => x.LeaderNumber == "L-B").Rank);
        Assert.Equal(21, all.TotalMatches);
        Assert.True(Assert.Single(all.Items, x => x.LeaderNumber == "L-C").InsufficientSample);
    }

    [Fact]
    public void 测试服可独立写入但榜单只读取正式服数据库()
    {
        var now = new DateTime(2026, 8, 7, 8, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempDir);
        var productionPath = Path.Combine(_tempDir, "production.db");
        var testPath = Path.Combine(_tempDir, "test.db");
        var productionStore = new LeaderStatsStore(productionPath);
        var testStore = new LeaderStatsStore(testPath, productionPath);

        productionStore.RecordMatch(Match(
            "production-match", now, MatchKind.Matchmaking, "L-PROD-A", "L-PROD-B", 0, 0, 8));
        testStore.RecordMatch(Match(
            "test-match", now, MatchKind.Matchmaking, "L-TEST-A", "L-TEST-B", 0, 0, 8));

        var result = testStore.GetLeaderboard("all", now);

        Assert.Equal(productionPath, testStore.LeaderboardDatabasePath);
        Assert.Equal(1, result.TotalMatches);
        Assert.Contains(result.Items, x => x.LeaderNumber == "L-PROD-A");
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "L-TEST-A");
        Assert.True(testStore.ContainsMatch("test-match"));
        Assert.False(testStore.ContainsMatch("production-match"));
    }

    [Fact]
    public void 对战前十统计覆盖双方位置先后手镜像和排名边界()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        const string target = "L-TARGET";

        for (var opponentIndex = 1; opponentIndex <= 9; opponentIndex++)
        {
            var opponent = $"L-{opponentIndex}";
            for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames; gameIndex++)
            {
                var targetIndex = gameIndex % 2;
                var targetWon = gameIndex < 12;
                var winner = targetWon ? targetIndex : 1 - targetIndex;
                var targetWentFirst = gameIndex % 4 < 2;
                var firstPlayer = targetWentFirst ? targetIndex : 1 - targetIndex;
                store.RecordMatch(Match(
                    $"top-{opponentIndex}-{gameIndex}",
                    now,
                    MatchKind.Matchmaking,
                    targetIndex == 0 ? target : opponent,
                    targetIndex == 1 ? target : opponent,
                    winner,
                    firstPlayer,
                    8));
            }
        }

        for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames; gameIndex++)
        {
            var firstPlayer = gameIndex % 2;
            var winner = gameIndex < 12 ? firstPlayer : 1 - firstPlayer;
            store.RecordMatch(Match(
                $"mirror-{gameIndex}", now, MatchKind.Friendly, target, target, winner, firstPlayer, 10));
        }

        for (var gameIndex = 0; gameIndex < LeaderStatsStore.MinimumRankedGames - 1; gameIndex++)
        {
            store.RecordMatch(Match(
                $"unranked-{gameIndex}", now, MatchKind.RoomCode, target, "L-10", 1, gameIndex % 2, 12));
        }

        var result = store.GetMatchups(target, "all", now);

        Assert.Equal(target, result.LeaderNumber);
        Assert.Equal(10, result.Items.Count);
        Assert.DoesNotContain(result.Items, x => x.LeaderNumber == "L-10");

        var opponentRow = Assert.Single(result.Items, x => x.LeaderNumber == "L-1");
        Assert.False(opponentRow.IsMirror);
        Assert.Equal(20, opponentRow.Games);
        Assert.Equal(12, opponentRow.Wins);
        Assert.Equal(8, opponentRow.Losses);
        Assert.Equal(0.6, opponentRow.WinRate!.Value, precision: 8);
        Assert.Equal(10, opponentRow.FirstGames);
        Assert.Equal(0.6, opponentRow.FirstWinRate!.Value, precision: 8);
        Assert.Equal(10, opponentRow.SecondGames);
        Assert.Equal(0.6, opponentRow.SecondWinRate!.Value, precision: 8);

        var mirrorRow = Assert.Single(result.Items, x => x.LeaderNumber == target);
        Assert.True(mirrorRow.IsMirror);
        Assert.Equal(20, mirrorRow.Games);
        Assert.Null(mirrorRow.WinRate);
        Assert.Null(mirrorRow.Wins);
        Assert.Null(mirrorRow.Losses);
        Assert.Equal(0.6, mirrorRow.FirstWinRate!.Value, precision: 8);
        Assert.Equal(0.4, mirrorRow.SecondWinRate!.Value, precision: 8);
    }

    [Fact]
    public void 个人详情按账号聚合胜负常用领航和趋势()
    {
        var now = new DateTime(2026, 8, 8, 8, 0, 0, DateTimeKind.Utc);
        var store = CreateStore();
        store.RecordMatch(new LeaderMatchResult(
            "profile-1", now.AddDays(-1), MatchKind.Matchmaking,
            "Alice", "Bob", "L-A", "L-B", 0, 0, 10, "胜利"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-2", now.AddDays(-2), MatchKind.RoomCode,
            "Bob", "Alice", "L-C", "L-A", 0, 0, 12, "失败"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-3", now.AddDays(-3), MatchKind.Friendly,
            "Alice", "Carol", "L-D", "L-C", 0, 1, 15, "胜利"));
        store.RecordMatch(new LeaderMatchResult(
            "profile-old", now.AddDays(-40), MatchKind.Matchmaking,
            "Alice", "Bob", "L-A", "L-B", 0, 0, 10, "旧对局"));

        var result = store.GetPlayerProfile("alice", "30d", now);

        Assert.Equal(3, result.Games);
        Assert.Equal(2, result.Wins);
        Assert.Equal(1, result.Losses);
        Assert.Equal(2d / 3d, result.WinRate, precision: 8);
        var favorite = Assert.Single(result.TopLeaders, item => item.LeaderNumber == "L-A");
        Assert.Equal(2, favorite.Games);
        Assert.Equal(1, favorite.Wins);
        Assert.Equal(10, result.Trend.Count);
        Assert.Equal(3, result.Trend.Sum(point => point.Games));

        var bob = store.GetPlayerProfile("Bob", "30d", now);
        Assert.Equal(2, bob.Games);
        Assert.Equal(2, bob.TopLeaders.Count);
    }

    private LeaderStatsStore CreateStore()
    {
        Directory.CreateDirectory(_tempDir);
        return new LeaderStatsStore(Path.Combine(_tempDir, "leader-stats.db"));
    }

    private static LeaderMatchResult Match(
        string id,
        DateTime endedAtUtc,
        MatchKind kind,
        string leader0,
        string leader1,
        int winner,
        int firstPlayer,
        int turnCount)
        => new(
            id,
            endedAtUtc,
            kind,
            "Alice",
            "Bob",
            leader0,
            leader1,
            winner,
            firstPlayer,
            turnCount,
            "测试结束");

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
