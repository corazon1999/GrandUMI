using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP15_092_MonkeyDLuffyTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 两张在场时领袖原本力量仍为七千()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var firstLuffy = Card("OP15-092");
        var secondLuffy = Card("OP15-092");
        me.Characters.Add(firstLuffy);
        me.Characters.Add(secondLuffy);
        for (int i = 0; i < 20; i++)
            me.Trash.Add(Card("OP15-003"));
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(
            state, 0, firstLuffy, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(7000, state.CurrentPowerOf(0, me.Leader));

        await EffectRuntime.Resolve(
            state, 0, secondLuffy, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(7000, state.CurrentPowerOf(0, me.Leader));
    }
}
