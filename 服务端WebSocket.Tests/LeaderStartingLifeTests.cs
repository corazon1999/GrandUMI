using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class LeaderStartingLifeTests
{
    [Fact]
    public void OP05_098_开局生命数为4()
    {
        TestScene.New(); // 加载卡牌数据库
        var deck = BuildLegalDeck("OP05-098");

        var engine = new GameEngine(
            "op05-098-starting-life",
            ("s0", "player0", deck),
            ("s1", "player1", deck),
            firstPlayer: 0,
            rngSeed: 20260811);

        Assert.Equal(4, engine.State.Players[0].LifeArea.Count);
        Assert.Equal(4, engine.State.Players[1].LifeArea.Count);
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP05")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;

        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }

        return string.Join('\n', lines);
    }
}
