using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class GameFeedback20260826Batch41To54Tests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task G895_EndTurnWithTwoTriggeredEffectsPromptsForOrderAndSettlesBoth()
    {
        const string deck = "OP14-001\nOP14-022\nOP14-023\nOP15-003";
        var engine = new GameEngine(
            "g895-end-turn-order",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 20260826);
        await engine.WaitSettledAsync();

        var state = engine.State;
        var me = state.Players[0];
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        me.Characters.Clear();
        me.CostArea.Clear();
        var don1 = new DonCard { State = DonState.Rest };
        var don2 = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([don1, don2]);
        var activeDonSource = Card("OP14-022");
        var activateSelfSource = Card("OP14-023");
        activateSelfSource.IsTapped = true;
        me.Characters.AddRange([activeDonSource, activateSelfSource]);

        Assert.True(engine.HandleAction(
            0,
            "EndTurn",
            JsonSerializer.SerializeToElement(new { })));
        for (var i = 0; i < 100 && state.PendingPrompt?.Kind != "EffectOrder"; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("EffectOrder", prompt.Kind);
        Assert.Equal(0, prompt.PlayerIndex);
        Assert.Equal(
            [activeDonSource.Id.ToString(), activateSelfSource.Id.ToString()],
            prompt.ValidChoices);

        Assert.True(engine.HandleAction(
            0,
            "PromptResponse",
            JsonSerializer.SerializeToElement(new
            {
                promptId = prompt.PromptId,
                chosen = new[] { activateSelfSource.Id.ToString() },
            })));
        await engine.WaitSettledAsync(resolvingPromptId: prompt.PromptId);

        Assert.Null(state.PendingPrompt);
        Assert.False(activateSelfSource.IsTapped);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        Assert.Equal(1, state.CurrentTurnPlayer);
        Assert.Equal(4, state.TurnCount);
        Assert.Equal(Phase.Main, state.Phase);
    }

    [Fact]
    public async Task G881_OP17_015_ReplacesOwnDeckBottomLeaveWithKoAndResolvesOnKo()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP17-015");
        var reviveCost = Card("OP17-017");
        me.Characters.Add(source);
        me.Hand.Add(reviveCost);
        var prompts = new MockPromptService()
            .QueueChoose(source.Id.ToString())
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(reviveCost.Id.ToString());

        await EffectRuntime.Resolve(state, 1, Card("OP17-046"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(source, me.Characters);
        Assert.DoesNotContain(source, me.Deck);
        Assert.DoesNotContain(source, me.Trash);
        Assert.Contains(reviveCost, me.Trash);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Empty(state.PendingKOEffects);
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task G884_EB02_003_OpponentTurnPowerBonusRequiresTwoAttachedDon()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB02-003");
        me.Characters.Add(source);
        var first = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        var second = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.AddRange([first, second]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        state.CurrentTurnPlayer = 1;
        Assert.Equal(2000, state.ContinuousPowerBonus(0, source));
        me.CostArea.Remove(second);
        Assert.Equal(0, state.ContinuousPowerBonus(0, source));
        me.CostArea.Add(second);
        source.IsEffectsNullified = true;
        Assert.Equal(0, state.ContinuousPowerBonus(0, source));
    }

    [Fact]
    public async Task G880_G898_OP06_003_PlayedCharacterResolvesItsOnEnterEffect()
    {
        var state = TestScene.New().MyDeckTop("OP06-002", "OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = Card("OP06-003");
        me.Characters.Add(source);
        var pulled = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(pulled.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(pulled, me.Characters);
        Assert.Contains(state.ContinuousEffects, effect => effect.SourceCardId == pulled.Id.ToString());
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task G892_OP06_080_PaysTwoActiveDonAndOneHandBeforeReceivingBenefit()
    {
        var state = TestScene.New("OP06-080").MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = me.Leader;
        var firstActive = new DonCard { State = DonState.Active };
        var secondActive = new DonCard { State = DonState.Active };
        var attached = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.AddRange([firstActive, secondActive, attached]);
        var discard = Card("OP15-005");
        var target = Card("OP06-082");
        me.Hand.Add(discard);
        me.Trash.Add(target);
        var milled = me.Deck.ToList();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(DonState.Rest, firstActive.State);
        Assert.Equal(DonState.Rest, secondActive.State);
        Assert.Equal(DonState.Attached, attached.State);
        Assert.Contains(discard, me.Trash);
        Assert.All(milled, card => Assert.Contains(card, me.Trash));
        Assert.Contains(target, me.Characters);
        Assert.DoesNotContain(target, me.Trash);
    }

    [Fact]
    public async Task G892_OP06_080_CancelledDiscardLeavesCompositeCostAndBenefitUntouched()
    {
        var state = TestScene.New("OP06-080").MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = me.Leader;
        var first = new DonCard { State = DonState.Active };
        var second = new DonCard { State = DonState.Active };
        var attached = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        var hand = Card("OP15-005");
        me.CostArea.AddRange([first, second, attached]);
        me.Hand.Add(hand);
        var originalDeck = me.Deck.ToList();
        var prompts = new MockPromptService().QueueConfirm(true).QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(DonState.Active, first.State);
        Assert.Equal(DonState.Active, second.State);
        Assert.Equal(DonState.Attached, attached.State);
        Assert.Contains(hand, me.Hand);
        Assert.Equal(originalDeck, me.Deck);
        Assert.Empty(me.Trash);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task G891_OP11_054_EachReturnedHandCardExplainsAndUsesItsFinalOrder(
        int firstPlacement,
        int secondPlacement)
    {
        var state = TestScene.New("OP06-001")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005", "OP15-008", "OP15-009")
            .Build();
        var me = state.Players[0];
        var first = Card("OP15-006");
        var second = Card("OP15-007");
        me.Hand.AddRange([first, second]);
        var deckAfterDraw = me.Deck.Skip(3).ToList();
        var source = Card("OP11-054");
        me.Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString())
            .QueueChoose(second.Id.ToString())
            .QueueOption(firstPlacement)
            .QueueOption(secondPlacement);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var expectedDeck = (firstPlacement, secondPlacement) switch
        {
            (0, 0) => new[] { first, second }.Concat(deckAfterDraw),
            (0, 1) => new[] { first }.Concat(deckAfterDraw).Append(second),
            (1, 0) => new[] { second }.Concat(deckAfterDraw).Append(first),
            _ => deckAfterDraw.Concat(new[] { first, second }),
        };
        Assert.Equal(expectedDeck, me.Deck);
        Assert.Equal(3, me.Hand.Count);
        Assert.Equal(2, prompts.ChooseHistory.Count);

        Assert.Contains("第 1/2 张", prompts.ChooseHistory[0].text);
        Assert.Contains("第 2/2 张", prompts.ChooseHistory[1].text);
        Assert.Contains(first.Info.Number, prompts.ChooseHistory[1].text);
        if (firstPlacement == 0)
        {
            Assert.Contains("已放牌顶", prompts.ChooseHistory[1].text);
            Assert.Contains("本张会位于第 1 张下方", prompts.ChooseHistory[1].text);
        }
        else
        {
            Assert.Contains("已放牌底", prompts.ChooseHistory[1].text);
            Assert.Contains("本张会位于第 1 张下方并成为最终最下方", prompts.ChooseHistory[1].text);
        }

        Assert.Equal(2, prompts.OptionHistory.Count);
        Assert.Contains(first.Info.Number, prompts.OptionHistory[0].text);
        Assert.Contains("本张最终最上", prompts.OptionHistory[0].options[0]);
        Assert.Contains("第 2 张放牌顶时本张最终最下", prompts.OptionHistory[0].options[1]);
        Assert.Contains("放牌底时第 2 张位于本张下方", prompts.OptionHistory[0].options[1]);
        Assert.Contains(second.Info.Number, prompts.OptionHistory[1].text);
        Assert.Contains(first.Info.Number, prompts.OptionHistory[1].text);
        Assert.Contains(firstPlacement == 0 ? "已放牌顶" : "已放牌底", prompts.OptionHistory[1].text);
        if (firstPlacement == 0)
        {
            Assert.Contains("本张在第 1 张下方", prompts.OptionHistory[1].options[0]);
            Assert.Contains("本张成为卡组最下方", prompts.OptionHistory[1].options[1]);
        }
        else
        {
            Assert.Contains("本张成为卡组最上方", prompts.OptionHistory[1].options[0]);
            Assert.Contains("本张在第 1 张下方", prompts.OptionHistory[1].options[1]);
        }
    }

    [Fact]
    public async Task G891_OP11_054_InvalidPlacementDoesNotMoveTheSelectedCard()
    {
        var state = TestScene.New("OP06-001")
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005")
            .Build();
        var me = state.Players[0];
        var selected = Card("OP15-006");
        me.Hand.Add(selected);
        var source = Card("OP11-054");
        me.Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString())
            .QueueOption(-1);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(selected, me.Hand);
        Assert.DoesNotContain(selected, me.Deck);
        Assert.Single(prompts.ChooseHistory);
        Assert.Single(prompts.OptionHistory);
    }

    [Fact]
    public async Task G896_OP08_007_PlayingEB02_003FromDeckResolvesItsOnEnterRegistration()
    {
        var state = TestScene.New()
            .MyDeckTop("EB02-003", "OP15-003", "OP15-004", "OP15-005", "OP15-006")
            .Build();
        state.CurrentTurnPlayer = 0;
        var me = state.Players[0];
        var source = Card("OP08-007");
        me.Characters.Add(source);
        var chopper = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(chopper.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(chopper, me.Characters);
        Assert.True(chopper.IsTapped);
        Assert.Contains(state.ContinuousEffects, effect => effect.SourceCardId == chopper.Id.ToString());
        Assert.Empty(state.PendingEnterFields);
    }

    [Fact]
    public async Task G909_OP13_082_CanPlayEligibleCharacterJustMovedFromFieldToTrash()
    {
        var state = TestScene.New("OP13-079").MyActiveDon(1).Build();
        var me = state.Players[0];
        var source = Card("OP13-082");
        var movedCandidate = Card("OP13-083");
        var discard = Card("OP15-003");
        me.Characters.AddRange([source, movedCandidate]);
        me.Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(movedCandidate.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(movedCandidate, me.Characters);
        Assert.DoesNotContain(movedCandidate, me.Trash);
        Assert.Contains(source, me.Trash);
        Assert.Contains(discard, me.Trash);
        var playPrompt = Assert.Single(prompts.ChooseHistory.Where(history =>
            history.kind == "OwnTrashCharacter" && history.choices.Contains(movedCandidate.Id.ToString())));
        Assert.Contains(movedCandidate.Id.ToString(), playPrompt.choices);
    }

    [Fact]
    public async Task G911_ST07_015_DataAndLifeTriggerUseSoulPocusMainEffect()
    {
        Assert.Equal(5, CardDatabase.Get("ST07-015")!.Cost);
        Assert.Equal(1, CardDatabase.Get("ST07-016")!.Cost);
        Assert.True(EffectRuntime.HasEffectForTrigger(Card("ST07-015"), EffectTrigger.OnLifeRevealTrigger));

        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var opponentLife = Card("OP15-004");
        state.Players[1].LifeArea.Add(opponentLife);
        var prompts = new MockPromptService().QueueOption(0);

        await EffectRuntime.Resolve(
            state, 0, Card("ST07-015"), EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Empty(state.Players[1].LifeArea);
        Assert.Contains(opponentLife, state.Players[1].Trash);
        Assert.Empty(state.Players[0].LifeArea);
        Assert.Single(state.Players[0].Deck);
    }

    [Fact]
    public async Task G897_OP16_089_DrawsTwoThenDiscardsExactlyTwo()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = Card("OP16-089");
        me.Characters.Add(source);
        var firstDiscard = Card("OP15-005");
        var secondDiscard = Card("OP15-006");
        me.Hand.AddRange([firstDiscard, secondDiscard]);
        var prompts = new MockPromptService()
            .QueueChoose(firstDiscard.Id.ToString(), secondDiscard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(me.Deck);
        Assert.Equal(2, me.Hand.Count);
        Assert.Contains(firstDiscard, me.Trash);
        Assert.Contains(secondDiscard, me.Trash);
    }

    [Fact]
    public async Task G914_OP17_054_CannotRestSourceMeansZeroCostAndZeroBenefit()
    {
        var state = TestScene.New().MyActiveDon(3).OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP17-054");
        me.Characters.Add(source);
        AtomicOps.AddRestriction(source, RestrictionKind.CannotBeRested, KeywordDuration.ThisTurn, 1);
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        Assert.False(source.IsTapped);
        Assert.False(target.HasRestriction(RestrictionKind.CannotAttack));
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task G915_OP09_119_CanReturnAnyPositiveDonCountThenDrawAndGainRush()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP09-119");
        me.Characters.Add(source);
        var first = new DonCard { State = DonState.Active };
        var second = new DonCard { State = DonState.Rest };
        var third = new DonCard { State = DonState.Attached, AttachedToCardId = source.Id };
        me.CostArea.AddRange([first, second, third]);
        var drawn = Assert.Single(me.Deck);
        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString(), third.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(2, me.DonDeck.Count);
        Assert.Contains(first, me.DonDeck);
        Assert.Contains(third, me.DonDeck);
        Assert.Equal(second, Assert.Single(me.CostArea));
        Assert.Contains(drawn, me.Hand);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(0, prompt.min);
        Assert.Equal(3, prompt.max);
    }

    [Fact]
    public async Task G915_OP09_119_SelectingZeroDonCancelsWithZeroMutation()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP09-119");
        me.Characters.Add(source);
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var deckTop = Assert.Single(me.Deck);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(don, Assert.Single(me.CostArea));
        Assert.Empty(me.DonDeck);
        Assert.Equal(deckTop, Assert.Single(me.Deck));
        Assert.Empty(me.Hand);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
    }

    [Fact]
    public async Task G915_OP09_119_DuplicateDonResponseIsRejectedAtomically()
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var source = Card("OP09-119");
        me.Characters.Add(source);
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString(), don.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(don, Assert.Single(me.CostArea));
        Assert.Empty(me.DonDeck);
        Assert.Single(me.Deck);
        Assert.Empty(me.Hand);
        Assert.False(ActionValidator.HasKeyword(state, source, "速攻"));
    }
}
