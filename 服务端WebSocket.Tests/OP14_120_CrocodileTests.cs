using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP14_120_CrocodileTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OnEnter_UsesCurrentCostForLock_AndDrawsForCostZero()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var crocodile = Card("OP14-120");
        var reducedCostCharacter = Card("OP17-022");
        reducedCostCharacter.CostModThisTurn = -10;
        opponent.Characters.Add(reducedCostCharacter);
        var drawCard = me.Deck[0];
        var prompts = new MockPromptService()
            .QueueChoose(reducedCostCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(0, state.CurrentCostOf(1, reducedCostCharacter));
        var restriction = Assert.Single(reducedCostCharacter.Restrictions,
            item => item.Kind == RestrictionKind.CannotAttack);
        Assert.Equal(KeywordDuration.UntilNextOpponentEndPhase, restriction.Duration);
        Assert.Equal(0, restriction.AppliedBySide);
        Assert.Contains(drawCard, me.Hand);
        Assert.Empty(me.Deck);
        Assert.Contains(reducedCostCharacter.Id.ToString(), prompts.ChooseHistory.Single().choices);
    }

    [Fact]
    public async Task OnEnter_DrawsWhenOpponentHasCurrentCostEightOrMore()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var crocodile = Card("OP14-120");
        opponent.Characters.Add(Card("OP17-022"));
        var drawCard = me.Deck[0];
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(drawCard, me.Hand);
        Assert.Empty(me.Deck);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OnEnter_DoesNotDrawWhenOpponentOnlyHasCurrentCostOneToSeven()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003")
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var crocodile = Card("OP14-120");
        var middleCostCharacter = Card("ST32-003");
        opponent.Characters.Add(middleCostCharacter);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(me.Hand);
        Assert.Single(me.Deck);
        Assert.Empty(middleCostCharacter.Restrictions);
    }

    [Fact]
    public async Task OnKO_DiscardsOneHandCard_AndPlaysSelfFromTrash()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var crocodile = Card("OP14-120");
        var discard = Card("OP15-003");
        me.Trash.Add(crocodile);
        me.Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnKO, prompts);

        Assert.Contains(crocodile, me.Characters);
        Assert.DoesNotContain(crocodile, me.Trash);
        Assert.False(crocodile.IsTapped);
        Assert.DoesNotContain(discard, me.Hand);
        Assert.Contains(discard, me.Trash);
    }

    [Fact]
    public async Task OP14_079_LeaderEffectKO_TriggersDiscardAndReplay()
    {
        var state = TestScene.New("OP14-079")
            .MyDeckTop("OP15-050", "OP15-051")
            .Build();
        var me = state.Players[0];
        var crocodile = Card("OP14-120");
        var discard = Card("OP15-003");
        me.Characters.Add(crocodile);
        me.Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(crocodile.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(crocodile, me.Characters);
        Assert.DoesNotContain(crocodile, me.Trash);
        Assert.DoesNotContain(discard, me.Hand);
        Assert.Contains(discard, me.Trash);
        Assert.Equal(3, prompts.ConfirmHistory.Count);
        Assert.Contains("卡组最上方", prompts.ConfirmHistory[1]);
        Assert.Contains("【KO时】", prompts.ConfirmHistory[2]);
    }

    [Fact]
    public async Task OnKO_WithoutHandCard_DoesNotPlaySelf()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var crocodile = Card("OP14-120");
        me.Trash.Add(crocodile);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnKO, prompts);

        Assert.Contains(crocodile, me.Trash);
        Assert.DoesNotContain(crocodile, me.Characters);
        Assert.Empty(prompts.ConfirmHistory);
    }
}
