using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP13EffectTests
{
    [Fact]
    public async Task OP13_004_CurrentCostAtLeast8_BuffsLeaderAndAllOwnCharacters()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .MyCharacter("OP15-003")
            .OppCharacter("OP15-003")
            .AttachDonToMyLeader(1)
            .Build();
        var me = state.Players[0];
        var currentCost8 = me.Characters[0];
        currentCost8.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, currentCost8));
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));
        Assert.All(me.Characters, card =>
            Assert.Equal(1000, state.ContinuousPowerBonus(0, card)));
        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP13_004_RequiresAttachedDonAndCurrentCostAtLeast8()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .Build();
        var me = state.Players[0];
        var character = me.Characters[0];
        character.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));

        me.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = me.Leader.Id,
        });
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));

        character.CostModThisTurn = 0;
        Assert.Equal(7, state.CurrentCostOf(0, character));
        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));
    }
}
