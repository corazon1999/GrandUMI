using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP09_107_RobinTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OnEnter_WithThreeOrMoreOpponentLife_TrashesOnlyTheTopLifeCard()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        var top = Card("OP15-050");
        var middle = Card("OP15-051");
        var bottom = Card("OP15-052");
        opponent.LifeArea.AddRange([top, middle, bottom]);

        await EffectRuntime.Resolve(
            state, 0, Card("OP09-107"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal([middle, bottom], opponent.LifeArea);
        Assert.Contains(top, opponent.Trash);
    }

    [Fact]
    public async Task OnEnter_WithFewerThanThreeOpponentLife_DoesNotMoveLife()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        var top = Card("OP15-050");
        var bottom = Card("OP15-051");
        opponent.LifeArea.AddRange([top, bottom]);

        await EffectRuntime.Resolve(
            state, 0, Card("OP09-107"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal([top, bottom], opponent.LifeArea);
        Assert.Empty(opponent.Trash);
    }
}
