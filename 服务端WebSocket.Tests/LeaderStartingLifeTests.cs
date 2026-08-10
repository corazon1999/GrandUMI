using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class LeaderStartingLifeTests
{
    [Theory]
    [InlineData("OP05-001")]
    [InlineData("OP05-002")]
    [InlineData("OP05-022")]
    [InlineData("OP05-098")]
    [InlineData("OP06-001")]
    [InlineData("OP06-021")]
    [InlineData("OP06-022")]
    [InlineData("OP06-042")]
    [InlineData("OP08-001")]
    [InlineData("OP08-002")]
    [InlineData("OP08-057")]
    public void 指定领航_开局生命数为4(string leaderNumber)
    {
        TestScene.New(); // 加载卡牌数据库
        var deck = BuildLegalDeck(leaderNumber);

        var engine = new GameEngine(
            $"{leaderNumber.ToLowerInvariant()}-starting-life",
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
        var pool = CardDatabase.GetBySet(leader.SetCode)
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
