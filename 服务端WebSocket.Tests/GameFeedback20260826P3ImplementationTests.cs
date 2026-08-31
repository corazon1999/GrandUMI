using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class GameFeedback20260826P3ImplementationTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task G715_OP12_037_MainCanRestTwoOpponentDonWithNoCharacterTarget()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        opponent.CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(opponent.CostArea.Select(don => don.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(
            state, 0, Card("OP12-037"), EffectTrigger.EventMain, prompts);

        Assert.Equal(3, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.All(opponent.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OpponentCharacterOrDon", prompt.kind);
        Assert.Equal(opponent.CostArea.Select(don => don.Id.ToString()).ToArray(), prompt.choices);
    }

    [Fact]
    public async Task G861_OP15_098_UsesOriginalPowerWhenProtectedCharacterIsDebuffed()
    {
        var state = TestScene.New("OP15-098").Build();
        var me = state.Players[0];
        var victim = Card("OP15-100");
        victim.PowerModThisTurn = -5000;
        var life = Card("OP15-003");
        me.Characters.Add(victim);
        me.LifeArea.Add(life);
        var bounceSource = Card("ST03-009");
        state.Players[1].Characters.Add(bounceSource);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        await EffectRuntime.Resolve(
            state, 1, bounceSource, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(6000, victim.Info.Power);
        Assert.Equal(1000, state.CurrentPowerOf(0, victim));
        Assert.Contains(victim, me.Characters);
        Assert.DoesNotContain(victim, me.Hand);
        Assert.Empty(me.LifeArea);
        Assert.Contains(life, me.Hand);
    }

    [Fact]
    public async Task G825_OP07_079_AttackEffectPaysMillCostAndReducesChosenCharacterCost()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005")
            .OppCharacter("OP15-003")
            .Build();
        var me = state.Players[0];
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP07-079"), EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(2, me.Trash.Count);
        Assert.Single(me.Deck);
        Assert.Equal(-1, target.CostModThisTurn);
    }

    [Fact]
    public async Task G865_OP17_012_KoCanPlayOneCostWhitebeardStageFromHand()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var existingLife = Card("OP15-003");
        var stage = Card("OP16-021");
        me.LifeArea.Add(existingLife);
        me.Hand.Add(stage);
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP17-012"), EffectTrigger.OnKO, prompts);

        Assert.Equal([existingLife], me.LifeArea);
        Assert.False(stage.IsLifeFaceUp);
        Assert.DoesNotContain(stage, me.Hand);
        Assert.Same(stage, me.StageCard);
    }
}
