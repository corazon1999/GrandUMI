using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class GameFeedback20260826P3QuickClosureTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task G614_OP15_116_WithEmptyLifeSkipsTrashAndContinuesRemainingEffects()
    {
        var state = TestScene.New("OP01-001").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var discard = Card("OP15-004");
        me.Hand.Add(discard);
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP15-116"), EffectTrigger.EventMain, prompts);

        Assert.Empty(me.Deck);
        Assert.Single(me.LifeArea);
        Assert.Contains(discard, me.Trash);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public void G630_OP11_023_DiscountRequiresAllThreePrintedConditions()
    {
        var state = TestScene.New("OP11-021").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var arlong = Card("OP11-023");
        me.Hand.Add(arlong);
        for (var i = 0; i < 3; i++)
            me.LifeArea.Add(Card("OP15-003"));

        Assert.Equal(7, state.HandPlayCost(0, arlong));

        opponent.Leader.IsTapped = true;
        for (var i = 0; i < 4; i++)
        {
            var rested = Card("OP15-003");
            rested.IsTapped = true;
            opponent.Characters.Add(rested);
        }

        Assert.Equal(4, state.HandPlayCost(0, arlong));
    }

    [Fact]
    public async Task G658_OP15_052_CanPayWithLeoToProtectLowOriginalPowerAlly()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var leo = Card("OP15-052");
        var protectedCharacter = Card("OP15-003");
        me.Characters.AddRange([leo, protectedCharacter]);
        var bounceSource = Card("ST03-009");
        state.Players[1].Characters.Add(bounceSource);
        var prompts = new MockPromptService()
            .QueueChoose(protectedCharacter.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(leo.Id.ToString());

        await EffectRuntime.Resolve(
            state, 1, bounceSource, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(protectedCharacter, me.Characters);
        Assert.DoesNotContain(protectedCharacter, me.Hand);
        Assert.DoesNotContain(leo, me.Characters);
        Assert.Equal(leo, me.Deck.Last());
    }

    [Fact]
    public async Task G663_OP12_112_LifeTriggerDrawsTwoWithMulticolorLeader()
    {
        var state = TestScene.New("OP10-001")
            .MyDeckTop("OP15-003", "OP15-004")
            .Build();

        await EffectRuntime.Resolve(
            state, 0, Card("OP12-112"), EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Empty(state.Players[0].Deck);
        Assert.Equal(2, state.Players[0].Hand.Count);
    }

    [Fact]
    public async Task G688_OP09_004_PowerAuraStopsImmediatelyWhenSourceLeavesField()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var source = Card("OP09-004");
        state.Players[0].Characters.Add(source);
        var target = Assert.Single(state.Players[1].Characters);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(-1000, state.ContinuousPowerBonus(1, target));

        AtomicOps.KO(state, 0, source);

        Assert.Equal(0, state.ContinuousPowerBonus(1, target));
    }

    [Fact]
    public async Task G788_G873_OP12_085_AddsThreeToOwnCostWithRevolutionaryLeader()
    {
        var state = TestScene.New("OP05-001").Build();
        var source = Card("OP12-085");
        state.Players[0].Characters.Add(source);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, source));

        source.IsEffectsNullified = true;
        Assert.Equal(5, state.CurrentCostOf(0, source));
        source.IsEffectsNullified = false;
        Assert.Equal(8, state.CurrentCostOf(0, source));

        AtomicOps.KO(state, 0, source);
        Assert.Equal(0, state.ContinuousCostBonus(0, source));
    }

    [Fact]
    public async Task G799_OP02_068_CounterDiscardsOneHandBeforeGrantingPower()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var discard = Card("OP15-003");
        me.Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP02-068"), EffectTrigger.EventCounter, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.DoesNotContain(discard, me.Hand);
        Assert.Equal(3000, me.Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task G799_OP02_068_DecliningDiscardCostCausesZeroMutation()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var hand = Card("OP15-003");
        me.Hand.Add(hand);

        await EffectRuntime.Resolve(
            state,
            0,
            Card("OP02-068"),
            EffectTrigger.EventCounter,
            new MockPromptService().QueueChooseEmpty());

        Assert.Equal(hand, Assert.Single(me.Hand));
        Assert.Empty(me.Trash);
        Assert.Equal(0, me.Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task G799_OP02_068_WithoutDiscardableHandCausesZeroMutation()
    {
        var state = TestScene.New().Build();

        await EffectRuntime.Resolve(
            state, 0, Card("OP02-068"), EffectTrigger.EventCounter, new MockPromptService());

        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task G824_ST33_003_CanReturnTwoEligibleCharactersToDeckBottom()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var discard = Card("OP15-003");
        var first = Card("OP15-004");
        var second = Card("OP15-004");
        me.Hand.Add(discard);
        opponent.Characters.AddRange([first, second]);
        var prompts = new MockPromptService()
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(first.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("ST33-003"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.Empty(opponent.Characters);
        Assert.Equal([first, second], opponent.Deck);
    }

    [Fact]
    public async Task G862_OP13_076_MainCanReduceOpponentPowerAfterPayingFiveDon()
    {
        var state = TestScene.New().MyActiveDon(5).OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var target = Assert.Single(state.Players[1].Characters);
        me.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = me.Leader.Id,
        });
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP13-076"), EffectTrigger.EventMain, prompts);

        Assert.Equal(5, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Equal(-8000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task G903_OP02_024_BuffsEdwardNewgateLeaderDuringOwnersTurn()
    {
        var state = TestScene.New("OP02-001").Build();
        var me = state.Players[0];
        me.LifeArea.Add(Card("OP15-003"));
        state.CurrentTurnPlayer = 0;
        var stage = Card("OP02-024");
        me.StageCard = stage;

        await EffectRuntime.Resolve(
            state, 0, stage, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task G903_OP02_024_DoesNotBuffDifferentWhitebeardPiratesLeader()
    {
        var state = TestScene.New("OP03-001").Build();
        var me = state.Players[0];
        me.LifeArea.Add(Card("OP15-003"));
        state.CurrentTurnPlayer = 0;
        var stage = Card("OP02-024");
        me.StageCard = stage;

        await EffectRuntime.Resolve(
            state, 0, stage, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(me.Leader.Info.Power, state.CurrentPowerOf(0, me.Leader));
    }
}
