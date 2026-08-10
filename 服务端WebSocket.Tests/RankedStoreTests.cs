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
            var initial = store.GetSnapshot("alice", "爱丽丝", now);

            Assert.Equal(0, initial.Profile.PlacementGames);
            Assert.Equal(RankedStore.PlacementRequired, initial.Profile.PlacementRequired);
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
    [InlineData(0, "青铜", 3)]
    [InlineData(599, "白银", 1)]
    [InlineData(899, "黄金", 1)]
    [InlineData(1499, "钻石", 1)]
    [InlineData(1500, "传奇", null)]
    public void 排位分段_边界映射正确(int points, string tier, int? division)
    {
        var actual = RankedStore.RankLabel(points);
        Assert.Equal(tier, actual.Tier);
        Assert.Equal(division, actual.Division);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
