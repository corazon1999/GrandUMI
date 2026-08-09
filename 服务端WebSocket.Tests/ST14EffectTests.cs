using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST14EffectTests
{
    [Theory]
    [InlineData(EffectTrigger.OnEnterField)]
    [InlineData(EffectTrigger.OnAttackDeclare)]
    public async Task ST14_007_Robin_ActivatesWhenCostBoostedToEight(EffectTrigger trigger)
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .AttachDonToMyLeader(1)
            .MyCharacter("ST14-007")
            .OppCharacter("OP15-050")
            .MyDeckTop("OP15-050")
            .Build();

        var robin = state.Players[0].Characters[0];
        var target = state.Players[1].Characters[0];
        var sunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        state.Players[0].StageCard = sunny;

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, robin));

        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, robin, trigger, prompts);

        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OpponentCharacter");
        Assert.Equal(-5, target.CostModThisTurn);
    }

    [Fact]
    public async Task ST14_001_Luffy_GainsPowerFromCharacterWithBoostedCost()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .AttachDonToMyLeader(1)
            .MyCharacter("ST14-007")
            .Build();

        var leader = state.Players[0].Leader;
        var robin = state.Players[0].Characters[0];
        robin.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, robin));
        Assert.Equal(
            leader.CurrentPower(state.Players[0].AttachedDonCount(leader.Id), true) + 1000,
            state.CurrentPowerOf(0, leader));
    }
}
