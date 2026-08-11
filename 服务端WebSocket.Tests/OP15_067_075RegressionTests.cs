using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP15_067_075RegressionTests
{
    [Fact]
    public async Task OP15_067_GainsRushAtSixDonAndKeepsEnterEffect()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-067")
            .MyActiveDon(7)
            .MyDeckTop("OP15-050")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(card => card.Info.Number == "OP15-067");
        source.TurnPlayed = 3;
        state.TurnCount = 3;
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.Main;
        int handBefore = me.Hand.Count;
        var prompts = new MockPromptService()
            .QueueChoose(me.CostArea[0].Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(6, me.TotalDonInCostArea);
        Assert.Equal(handBefore + 1, me.Hand.Count);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, source.Id, targetIsLeader: true, targetId: null).Ok);

        var seventhDon = new DonCard { State = DonState.Active };
        me.CostArea.Add(seventhDon);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
        Assert.False(ActionValidator.CanAttack(state, 0, source.Id, targetIsLeader: true, targetId: null).Ok);

        me.CostArea.Remove(seventhDon);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Fact]
    public async Task OP15_075_CanKoCharacterReducedToThreeThousandPower()
    {
        var state = TestScene.New(myLeaderNumber: "OP15-058")
            .MyActiveDon(2)
            .OppCharacter("OP03-004")
            .OppCharacter("OP03-004")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var reducedTarget = opponent.Characters[0];
        var unreducedTarget = opponent.Characters[1];
        reducedTarget.PowerModThisTurn = -1000;
        Assert.Equal(3000, state.CurrentPowerOf(1, reducedTarget));

        var prompts = new MockPromptService()
            .QueueChoose(me.CostArea[0].Id.ToString())
            .QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(reducedTarget.Id.ToString());
        var source = new CardInstance { Info = CardDatabase.Get("OP15-075")! };

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(history => history.kind == "OpponentCharacter"));
        Assert.Contains(reducedTarget.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(unreducedTarget.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(reducedTarget, opponent.Characters);
        Assert.Contains(reducedTarget, opponent.Trash);
    }
}
