using GrandUMI.Game.Ranked;
using Xunit;

namespace GrandUMI.Tests;

public class RankedStoreTests
{
    [Fact]
    public void 排位结算_五局完成定级且同一对局只结算一次()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            var initial = store.SelectFaction("alice", "爱丽丝", RankedStore.PirateFaction, now)!;

            Assert.Equal(0, initial.Profile.PlacementGames);
            Assert.Equal(RankedStore.PlacementRequired, initial.Profile.PlacementRequired);
            Assert.Equal(RankedStore.PirateFaction, initial.Profile.Faction);
            Assert.Empty(initial.Leaderboard);

            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                var result = store.RecordMatch($"ranked-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0);
                Assert.NotNull(result);
            }

            Assert.Null(store.RecordMatch("ranked-4", now.AddMinutes(10),
                "alice", "爱丽丝", "bob-4", "对手4", winnerIndex: 0));

            var settled = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(20));
            Assert.Equal(5, settled.Profile.PlacementGames);
            Assert.Equal(5, settled.Profile.Games);
            Assert.Equal(5, settled.Profile.Wins);
            Assert.Equal(0, settled.Profile.Losses);
            Assert.InRange(settled.Profile.RankPoints, 0, 899);
            Assert.Contains(settled.Leaderboard, item => item.DisplayName == "爱丽丝" && item.Games == 5);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Theory]
    [InlineData(0, RankedStore.PirateFaction, "见习海贼", 3, null)]
    [InlineData(599, RankedStore.PirateFaction, "海贼战斗员", 1, null)]
    [InlineData(899, RankedStore.MarineFaction, "海军少校", 1, null)]
    [InlineData(1499, RankedStore.GovernmentFaction, "神之骑士团", 1, null)]
    [InlineData(1500, RankedStore.PirateFaction, "海贼王", null, 1)]
    [InlineData(1500, RankedStore.PirateFaction, "四皇", null, 5)]
    [InlineData(1500, RankedStore.MarineFaction, "海军大将", null, 4)]
    [InlineData(1500, RankedStore.GovernmentFaction, "五老星", null, 6)]
    public void 排位分段_阵营称号映射正确(int points, string faction, string tier, int? division, int? factionRank)
    {
        var actual = RankedStore.RankLabel(points, faction, factionRank);
        Assert.Equal(tier, actual.Tier);
        Assert.Equal(division, actual.Division);
    }

    [Fact]
    public void 阵营选择_确认更换后清空本赛季排位进度()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

            var selected = store.SelectFaction("alice", "爱丽丝", RankedStore.MarineFaction, now)!;
            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                Assert.NotNull(store.RecordMatch($"ranked-reset-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0));
            }

            var beforeReset = store.GetSnapshot("alice", "爱丽丝", now.AddMinutes(10));
            var unconfirmed = store.SelectFaction("alice", "爱丽丝", RankedStore.GovernmentFaction, now.AddMinutes(11));
            var changed = store.SelectFaction("alice", "爱丽丝", RankedStore.GovernmentFaction, now.AddMinutes(12),
                resetRankProgress: true);

            Assert.Equal(RankedStore.MarineFaction, selected.Profile.Faction);
            Assert.Equal(RankedStore.PlacementRequired, beforeReset.Profile.Games);
            Assert.NotNull(unconfirmed);
            Assert.Equal(RankedStore.MarineFaction, unconfirmed!.Profile.Faction);
            Assert.Equal(beforeReset.Profile.RankPoints, unconfirmed.Profile.RankPoints);
            Assert.NotNull(changed);
            Assert.Equal(RankedStore.GovernmentFaction, changed!.Profile.Faction);
            Assert.Equal(0, changed.Profile.RankPoints);
            Assert.Equal(0, changed.Profile.HighestRankPoints);
            Assert.Equal(0, changed.Profile.PlacementGames);
            Assert.Equal(0, changed.Profile.Games);
            Assert.Equal(0, changed.Profile.Wins);
            Assert.Equal(0, changed.Profile.Losses);
            Assert.Equal(1500, store.GetMatchRating("alice", "爱丽丝", now.AddMinutes(13)));
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_连续获胜三场后记录连胜且失败会中断()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

            for (var i = 1; i <= 3; i++)
            {
                var result = store.RecordMatch($"streak-win-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", $"bob-{i}", $"对手{i}", winnerIndex: 0);
                Assert.NotNull(result);
                Assert.Equal(i - 1, result!.Player0.WinStreakBefore);
                Assert.Equal(i, result!.Player0.WinStreak);
                Assert.Equal(0, result.Player1.WinStreak);
            }

            var loss = store.RecordMatch("streak-loss", now.AddMinutes(4),
                "alice", "爱丽丝", "bob-loss", "对手", winnerIndex: 1);
            Assert.NotNull(loss);
            Assert.Equal(3, loss!.Player0.WinStreakBefore);
            Assert.Equal(0, loss!.Player0.WinStreak);
            Assert.Equal(0, loss.Player1.WinStreakBefore);
            Assert.Equal(1, loss.Player1.WinStreak);

            var winAfterLoss = store.RecordMatch("streak-restart", now.AddMinutes(5),
                "alice", "爱丽丝", "bob-restart", "对手", winnerIndex: 0);
            Assert.NotNull(winAfterLoss);
            Assert.Equal(1, winAfterLoss!.Player0.WinStreak);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public void 排位结算_定级后胜负固定对称加减二十分()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

            // 交替胜负完成双方定级，使双方都处在无段位保护影响的正常结算区间。
            for (var i = 0; i < RankedStore.PlacementRequired; i++)
            {
                Assert.NotNull(store.RecordMatch($"placement-{i}", now.AddMinutes(i),
                    "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: i % 2));
            }

            var aliceWins = store.RecordMatch("fixed-rp-alice-win", now.AddMinutes(10),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 0);
            Assert.NotNull(aliceWins);
            Assert.Equal(20, aliceWins!.Player0.RankPointDelta);
            Assert.Equal(-20, aliceWins.Player1.RankPointDelta);

            var bobWins = store.RecordMatch("fixed-rp-bob-win", now.AddMinutes(11),
                "alice", "爱丽丝", "bob", "鲍勃", winnerIndex: 1);
            Assert.NotNull(bobWins);
            Assert.Equal(-20, bobWins!.Player0.RankPointDelta);
            Assert.Equal(20, bobWins.Player1.RankPointDelta);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
