using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class ResidualEffectBatchTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EB03_053_OnKO_FlipsTopLifeAndPlaysPowerSixThousandCharacter()
    {
        var state = TestScene.New().Build();
        var nami = Card("EB03-053");
        var life = Card("ST30-002");
        var valid = Card("EB03-002");
        var invalid = Card("EB01-018");
        state.Players[0].Trash.Add(nami);
        state.Players[0].LifeArea.Add(life);
        state.Players[0].Hand.Add(valid);
        state.Players[0].Hand.Add(invalid);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(valid.Id.ToString());

        await EffectRuntime.Resolve(state, 0, nami, EffectTrigger.OnKO, prompts);

        Assert.True(life.IsLifeFaceUp);
        Assert.Contains(valid, state.Players[0].Characters);
        Assert.DoesNotContain(valid, state.Players[0].Hand);
        Assert.Contains(invalid, state.Players[0].Hand);
    }

    [Fact]
    public async Task EB03_053_OnEnterField_ClearsFaceUpStateWhenOpponentLifeMovesToHand()
    {
        var state = TestScene.New().Build();
        var nami = Card("EB03-053");
        var top = Card("ST30-002");
        top.IsLifeFaceUp = true;
        state.Players[0].Characters.Add(nami);
        state.Players[1].LifeArea.Add(top);
        state.Players[1].LifeArea.Add(Card("ST30-003"));
        state.Players[1].LifeArea.Add(Card("ST30-004"));

        await EffectRuntime.Resolve(state, 0, nami, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(top, state.Players[1].Hand);
        Assert.DoesNotContain(top, state.Players[1].LifeArea);
        Assert.False(top.IsLifeFaceUp);
    }

    [Fact]
    public async Task EB04_059_PaysFaceUpLifeCost_AndUsesFullEffectKOFlowTwice()
    {
        var state = TestScene.New().Build();
        var life = Card("ST30-002");
        var costSix = Card("EB03-019");
        var costFive = Card("EB01-018");
        state.Players[0].LifeArea.Add(life);
        state.Players[1].Characters.Add(costSix);
        state.Players[1].Characters.Add(costFive);
        var card = Card("EB04-059");
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(costSix.Id.ToString())
            .QueueChoose(costFive.Id.ToString());

        await EffectRuntime.Resolve(state, 0, card, EffectTrigger.EventMain, prompts);

        Assert.True(life.IsLifeFaceUp);
        Assert.Empty(state.Players[1].Characters);
        Assert.Contains(costSix, state.Players[1].Trash);
        Assert.Contains(costFive, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP02_064_ReturnsSelfAtBattleEnd_EvenWhenNoBounceTargetWasChosen()
    {
        var state = TestScene.New().Build();
        var bonClay = Card("OP02-064");
        var discard = Card("ST30-002");
        state.Players[0].Characters.Add(bonClay);
        state.Players[0].Hand.Add(discard);
        var attachedDon = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = bonClay.Id,
        };
        state.Players[0].CostArea.Add(attachedDon);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        Assert.True(EffectRuntime.HasEffectForTrigger(bonClay, EffectTrigger.OnBattleEnd));
        await EffectRuntime.Resolve(state, 0, bonClay, EffectTrigger.OnAttackDeclare, prompts);
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnBattleEnd, prompts,
            new Dictionary<string, object?> { ["attackerId"] = bonClay.Id.ToString() });

        Assert.Contains(discard, state.Players[0].Trash);
        Assert.DoesNotContain(bonClay, state.Players[0].Characters);
        Assert.Equal(bonClay, state.Players[0].Deck.Last());
        Assert.Equal(DonState.Rest, attachedDon.State);
        Assert.Null(attachedDon.AttachedToCardId);
    }

    [Fact]
    public async Task OP06_020_BlocksEffectLifeToHandUntilEndPhase()
    {
        var state = TestScene.New(myLeaderNumber: "OP06-020").OppActiveDon(1).Build();
        var hody = state.Players[0].Leader;
        var life = Card("ST30-002");
        var sanji = Card("OP01-013");
        state.Players[0].LifeArea.Add(life);
        state.Players[0].Characters.Add(sanji);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueOption(1);

        await EffectRuntime.Resolve(state, 0, hody, EffectTrigger.ActivatedMain, prompts);

        Assert.True(hody.IsTapped);
        Assert.Equal(DonState.Rest, Assert.Single(state.Players[1].CostArea).State);
        Assert.Contains(0, state.NoEffectLifeToHandThisTurn);

        await EffectRuntime.Resolve(state, 0, sanji, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Contains(life, state.Players[0].LifeArea);
        Assert.DoesNotContain(life, state.Players[0].Hand);

        TurnEngine.EnterEndPhase(state);
        Assert.DoesNotContain(0, state.NoEffectLifeToHandThisTurn);
    }
}
