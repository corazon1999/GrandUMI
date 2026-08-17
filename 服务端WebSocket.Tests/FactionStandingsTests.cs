using GrandUMI.Game.Ranked;
using Xunit;

namespace GrandUMI.Tests;

public class FactionStandingsTests
{
    [Fact]
    public void 阵营榜_wire包含总分与内部排名字段()
    {
        var standings = RankWire.FactionStandings([
            new FactionStanding(1, RankedStore.PirateFaction, 12_345, 8, 40, 23),
        ]);
        var json = System.Text.Json.JsonSerializer.Serialize(standings);

        Assert.Contains("totalRankPoints", json);
        Assert.Contains("playerCount", json);
        Assert.Contains("pirate", json);
    }
}
