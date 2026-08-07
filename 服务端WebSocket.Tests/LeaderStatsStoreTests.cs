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
