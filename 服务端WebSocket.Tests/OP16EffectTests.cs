using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP16EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP16_003_BuffsOnlyLeaderDuringOwnerTurn()
    {
        var state = TestScene.New("OP16-001").Build();
        var newgate = Card("OP16-003");
        state.Players[0].Characters.Add(newgate);

        Assert.DoesNotContain("双重攻击", newgate.Info.Abilities);

        await EffectRuntime.Resolve(
            state,
            0,
            newgate,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        var leader = state.Players[0].Leader;
        Assert.True(ActionValidator.HasKeyword(state, leader, "双重攻击"));
        Assert.False(ActionValidator.HasKeyword(state, newgate, "双重攻击"));
        Assert.Equal(7000, state.CurrentPowerOf(0, leader));
        Assert.Equal(10000, state.CurrentPowerOf(0, newgate));

        state.CurrentTurnPlayer = 1;

        Assert.False(ActionValidator.HasKeyword(state, leader, "双重攻击"));
        Assert.Equal(5000, state.CurrentPowerOf(0, leader));
        Assert.Equal(10000, state.CurrentPowerOf(0, newgate));
    }

    [Fact]
    public void OP16_118_Changes8000PowerCharactersInHandToCounter2000()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var counter1000 = Card("OP16-017");
        var counter0 = Card("OP16-011");
        var non8000 = Card("OP16-009");

        state.Players[0].Characters.Add(ace);
        state.Players[0].Hand.AddRange([counter1000, counter0, non8000]);

        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter1000));
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, counter0));
        Assert.Equal(non8000.Info.Counter, HandStaticCounter.Value(state, 0, non8000));
    }

    [Fact]
    public void OP16_118_StopsChangingCountersAfterLeavingFieldOrBeingNullified()
    {
        var state = TestScene.New("OP16-001").Build();
        var ace = Card("OP16-118");
        var target = Card("OP16-017");

        state.Players[0].Characters.Add(ace);
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = true;
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));

        ace.IsEffectsNullified = false;
        state.Players[0].Characters.Remove(ace);
        Assert.Equal(target.Info.Counter, HandStaticCounter.Value(state, 0, target));
    }
}
