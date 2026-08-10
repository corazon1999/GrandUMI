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
    public void 阵营选择_首次选择后永久锁定()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grandumi-ranked-{Guid.NewGuid():N}.db");
        try
        {
            var store = new RankedStore(path);
            var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

            var selected = store.SelectFaction("alice", "爱丽丝", RankedStore.MarineFaction, now);
            var retry = store.SelectFaction("alice", "爱丽丝", RankedStore.GovernmentFaction, now.AddMinutes(1));

            Assert.NotNull(selected);
            Assert.NotNull(retry);
            Assert.Equal(RankedStore.MarineFaction, selected!.Profile.Faction);
            Assert.Equal(RankedStore.MarineFaction, retry!.Profile.Faction);
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
