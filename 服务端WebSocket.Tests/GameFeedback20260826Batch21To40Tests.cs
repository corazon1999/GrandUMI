using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class GameFeedback20260826Batch21To40Tests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task G796_OP11_100_FlipsFaceUpLifeTopFaceDownThenDrawsOne()
    {
        var state = TestScene.New("OP11-022").MyDeckTop("OP15-050").Build();
        var me = state.Players[0];
        var source = Card("OP11-100");
        var lifeTop = Card("OP15-051");
        lifeTop.IsLifeFaceUp = true;
        me.LifeArea.Add(lifeTop);
        me.Characters.Add(source);
        var drawn = Assert.Single(me.Deck);
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.False(lifeTop.IsLifeFaceUp);
        Assert.Contains(drawn, me.Hand);
        Assert.Empty(me.Deck);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task G800_OP17_045_DiscardsTwoToPreventOP08_069MovingItToLife()
    {
        var state = TestScene.New().Build();
        var defender = state.Players[0];
        var attacker = state.Players[1];
        var guarded = Card("OP17-045");
        var firstGuardCost = Card("OP15-050");
        var secondGuardCost = Card("OP15-051");
        defender.Characters.Add(guarded);
        defender.Hand.AddRange([firstGuardCost, secondGuardCost]);
        var donCost = new DonCard { State = DonState.Active };
        var discardCost = Card("OP15-052");
        var newLife = Card("OP15-053");
        attacker.CostArea.Add(donCost);
        attacker.Hand.Add(discardCost);
        attacker.Deck.Add(newLife);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(donCost.Id.ToString())
            .QueueChoose(discardCost.Id.ToString())
            .QueueChoose(guarded.Id.ToString())
            .QueueChoose(firstGuardCost.Id.ToString(), secondGuardCost.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 1, Card("OP08-069"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(guarded, defender.Characters);
        Assert.DoesNotContain(guarded, defender.LifeArea);
        Assert.DoesNotContain(guarded, defender.Trash);
        Assert.Empty(defender.Hand);
        Assert.Contains(firstGuardCost, defender.Trash);
        Assert.Contains(secondGuardCost, defender.Trash);
        Assert.Equal(newLife, Assert.Single(attacker.LifeArea));
        Assert.Contains(discardCost, attacker.Trash);
        Assert.Contains(donCost, attacker.DonDeck);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
    }

    [Fact]
    public async Task G806_ST31_001_WithTwoAttachedDonHasRushOnEntryTurn()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 3;
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.Main;
        var me = state.Players[0];
        var source = Card("ST31-001");
        source.TurnPlayed = state.TurnCount;
        me.Characters.Add(source);
        me.CostArea.AddRange([
            new DonCard { State = DonState.Attached, AttachedToCardId = source.Id },
            new DonCard { State = DonState.Attached, AttachedToCardId = source.Id },
        ]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(2, me.AttachedDonCount(source.Id));
        Assert.True(state.HasContinuousKeyword(source, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, source.Id, targetIsLeader: true, targetId: null).Ok);
    }

    [Fact]
    public async Task G813_PRB02_015_KoUsesOpponentOriginalCostNotCurrentCost()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("PRB02-015");
        var originalFourRaisedToNine = Card("OP09-082");
        var originalFiveReducedToZero = Card("OP09-083");
        originalFourRaisedToNine.CostModThisTurn = 5;
        originalFiveReducedToZero.CostModThisTurn = -5;
        me.Trash.Add(source);
        opponent.Characters.AddRange([originalFourRaisedToNine, originalFiveReducedToZero]);
        var prompts = new MockPromptService().QueueChoose(originalFourRaisedToNine.Id.ToString());

        Assert.Equal(9, state.CurrentCostOf(1, originalFourRaisedToNine));
        Assert.Equal(0, state.CurrentCostOf(1, originalFiveReducedToZero));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(originalFourRaisedToNine.Id.ToString(), choice.choices);
        Assert.DoesNotContain(originalFiveReducedToZero.Id.ToString(), choice.choices);
        Assert.Contains(originalFourRaisedToNine, opponent.Trash);
        Assert.Contains(originalFiveReducedToZero, opponent.Characters);
    }

    [Fact]
    public async Task G814_PRB02_015_GainsBlockerAndPlusFourCostWithBlackbeardLeader()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var source = Card("PRB02-015");
        me.Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, source));
        Assert.True(state.HasContinuousKeyword(source, "阻挡者"));
        state.CurrentTurnPlayer = 1;
        state.Phase = Phase.BattleBlock;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };
        Assert.True(ActionValidator.CanDeclareBlocker(state, 0, source.Id).Ok);
    }

    [Fact]
    public async Task G817_OP06_041_LifeTriggerPlaysStageOnceAndResolvesOnEnter()
    {
        var engine = CreateAttackEngine("OP01-001", "OP01-001");
        await engine.WaitSettledAsync();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        var noah = Card("OP06-041");
        var replacedStage = Card("OP09-099");
        var firstOpponent = Card("OP15-003");
        var secondOpponent = Card("OP15-004");
        me.LifeArea.Clear();
        me.Trash.Clear();
        me.StageCard = replacedStage;
        me.LifeArea.Add(noah);
        opponent.Characters.Clear();
        opponent.Characters.AddRange([firstOpponent, secondOpponent]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var prompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(prompt.PromptId, ["trigger"]);
        await damage;

        Assert.Same(noah, me.StageCard);
        Assert.DoesNotContain(noah, me.Trash);
        Assert.Equal(1, me.Trash.Count(card => ReferenceEquals(card, replacedStage)));
        Assert.All(opponent.Characters, card => Assert.True(card.IsTapped));
        Assert.Empty(state.PendingEnterFields);
        Assert.Null(state.PendingPrompt);
    }

    [Fact]
    public async Task G807_OP16_093_DrawsTwoDiscardsTwoThenAttachesOneRestedDon()
    {
        var state = TestScene.New().MyDeckTop("OP15-050", "OP15-051").Build();
        var me = state.Players[0];
        var source = Card("OP16-093");
        var target = Card("OP16-094");
        var firstDiscard = Card("OP15-052");
        var secondDiscard = Card("OP15-053");
        var restedDon = new DonCard { State = DonState.Rest };
        me.Characters.AddRange([source, target]);
        me.Hand.AddRange([firstDiscard, secondDiscard]);
        me.CostArea.Add(restedDon);
        var prompts = new MockPromptService()
            .QueueChoose(firstDiscard.Id.ToString(), secondDiscard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(firstDiscard, me.Trash);
        Assert.Contains(secondDiscard, me.Trash);
        Assert.Equal(2, me.Hand.Count);
        Assert.Equal(DonState.Attached, restedDon.State);
        Assert.Equal(target.Id, restedDon.AttachedToCardId);
        Assert.Equal(["DiscardOwnChosen", "OwnLeaderOrCharacter"],
            prompts.ChooseHistory.Select(item => item.kind));
    }

    [Fact]
    public async Task G864_OP17_012_KoPlacesCostOneFormerWhitebeardCardFaceUpOnLifeTop()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP17-012");
        var existingLife = Card("OP15-050");
        var formerWhitebeard = Card("EB01-005");
        me.Trash.Add(source);
        me.LifeArea.Add(existingLife);
        me.Hand.Add(formerWhitebeard);
        var prompts = new MockPromptService().QueueChoose(formerWhitebeard.Id.ToString());

        Assert.False(formerWhitebeard.Info.HasKeyword("白胡子海盗团"));
        Assert.True(formerWhitebeard.Info.HasKeywordContaining("白胡子海盗团"));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        Assert.Equal([formerWhitebeard, existingLife], me.LifeArea);
        Assert.True(formerWhitebeard.IsLifeFaceUp);
        Assert.DoesNotContain(formerWhitebeard, me.Hand);
        Assert.DoesNotContain(formerWhitebeard, me.Characters);
        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnHand", choice.kind);
    }

    [Fact]
    public async Task G842_OP08_043_DoesNotTaxLeaderAttackWithEmptyHand()
    {
        var engine = CreateAttackEngine("OP02-001", "OP01-001");
        await engine.WaitSettledAsync();
        var state = engine.State;
        var defender = state.Players[0];
        var attacker = state.Players[1];
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        defender.LifeArea.Clear();
        defender.LifeArea.AddRange([Card("OP15-050"), Card("OP15-051")]);
        attacker.Hand.Clear();
        attacker.Leader.IsTapped = false;
        var source = Card("OP08-043");
        defender.Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, engine.Prompts);
        Assert.Equal(2, state.AttackTaxDiscard[1]);

        Assert.True(engine.HandleAction(1, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = attacker.Leader.Id.ToString(),
            targetIsLeader = true,
        })));

        Assert.True(attacker.Leader.IsTapped);
        Assert.NotNull(state.CurrentBattle);
        Assert.Equal(attacker.Leader.Id, state.CurrentBattle!.AttackerCardId);
        Assert.Null(state.PendingPrompt);
    }

    [Fact]
    public async Task G842_OP08_043_StillTaxesCharacterAttackExactlyTwoCards()
    {
        var engine = CreateAttackEngine("OP02-001", "OP01-001");
        await engine.WaitSettledAsync();
        var state = engine.State;
        var defender = state.Players[0];
        var attacker = state.Players[1];
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        defender.LifeArea.Clear();
        var source = Card("OP08-043");
        defender.Characters.Add(source);
        var attackingCharacter = Card("OP15-003");
        attackingCharacter.TurnPlayed = 1;
        attacker.Characters.Clear();
        attacker.Characters.Add(attackingCharacter);
        attacker.Hand.Clear();
        var first = Card("OP15-050");
        var second = Card("OP15-051");
        attacker.Hand.AddRange([first, second]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, engine.Prompts);
        Assert.True(engine.HandleAction(1, "Attack", JsonSerializer.SerializeToElement(new
        {
            attackerId = attackingCharacter.Id.ToString(),
            targetIsLeader = true,
        })));

        var prompt = await WaitForPrompt(engine, "AttackTaxDiscard");
        Assert.Null(state.CurrentBattle);
        Assert.True(engine.HandleAction(1, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { first.Id.ToString(), second.Id.ToString() },
        })));
        await engine.WaitSettledAsync();

        Assert.Empty(attacker.Hand);
        Assert.Contains(first, attacker.Trash);
        Assert.Contains(second, attacker.Trash);
        Assert.True(attackingCharacter.IsTapped);
    }

    [Fact]
    public async Task G797_EB04_004_OriginalPowerChangeSurvivesSourceLeavingUntilOpponentEnd()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("EB04-004");
        me.Characters.Add(source);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnAttackDeclare, new MockPromptService());

        Assert.Equal(7000, state.OriginalPowerOf(0, me.Leader));
        var change = Assert.Single(me.Leader.OriginalPowerOverridesUntilOppEnd);
        Assert.Equal(7000, change.Value);
        Assert.Equal(0, change.AppliedBySide);

        AtomicOps.KO(state, 0, source);
        Assert.DoesNotContain(source, me.Characters);
        Assert.Equal(7000, state.OriginalPowerOf(0, me.Leader));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(7000, state.OriginalPowerOf(0, me.Leader));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(me.Leader.Info.Power, state.OriginalPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task G870_OP08_118_PowerReductionsLastUntilOpponentEnd()
    {
        var state = TestScene.New().Build();
        var source = Card("OP08-118");
        var first = Card("OP17-047");
        var second = Card("OP17-100");
        state.Players[0].Characters.Add(source);
        state.Players[1].Characters.AddRange([first, second]);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString()));

        Assert.Equal(0, first.PowerModThisTurn);
        Assert.Equal(0, second.PowerModThisTurn);
        Assert.Equal(-3000, Assert.Single(first.PowerModsUntilOppEnd).Delta);
        Assert.Equal(-2000, Assert.Single(second.PowerModsUntilOppEnd).Delta);

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(first.Info.Power - 3000, state.CurrentPowerOf(1, first));
        Assert.Equal(second.Info.Power - 2000, state.CurrentPowerOf(1, second));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(first.Info.Power, state.CurrentPowerOf(1, first));
        Assert.Equal(second.Info.Power, state.CurrentPowerOf(1, second));
    }

    [Fact]
    public async Task G811_OP17_042_AcceptsFormerRocksPiratesTraitsWhenRevealingThreeCards()
    {
        var state = TestScene.New().OppCharacter("OP17-011").Build();
        var me = state.Players[0];
        var source = Card("OP17-042");
        var target = Assert.Single(state.Players[1].Characters);
        var formerRocksCards = new[]
        {
            Card("OP07-082"),
            Card("OP08-051"),
            Card("OP08-069"),
        };
        me.Characters.Add(source);
        me.Hand.AddRange(formerRocksCards);

        Assert.All(formerRocksCards, card =>
        {
            Assert.False(card.Info.HasKeyword("洛克斯海盗团"));
            Assert.True(card.Info.HasKeywordContaining("洛克斯海盗团"));
        });
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(formerRocksCards.Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(-3000, target.PowerModThisTurn);
        Assert.Equal(formerRocksCards, me.Hand);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Equal(2, prompts.ChooseHistory.Count);
    }

    [Fact]
    public async Task G819_OP10_063_AcceptsGerma66LeaderTraitAndSearchesGermaCard()
    {
        var state = TestScene.New("OP06-042").MyDeckTop("OP10-064").Build();
        var me = state.Players[0];
        var source = Card("OP10-063");
        var searched = Assert.Single(me.Deck);
        me.Characters.Add(source);

        Assert.False(me.Leader.Info.HasKeyword("GERMA"));
        Assert.True(me.Leader.Info.HasKeywordContaining("GERMA"));
        Assert.True(searched.Info.HasKeywordContaining("GERMA"));
        var prompts = new MockPromptService().QueueChoose(searched.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var search = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LookTopReveal", search.kind);
        Assert.Contains(searched.Id.ToString(), search.choices);
        Assert.Contains(searched, me.Hand);
        Assert.DoesNotContain(searched, me.Deck);
    }

    [Fact]
    public async Task G823_EB02_025_PlayedDeckCharacterResolvesOnEnterExactlyOnce()
    {
        var state = TestScene.New("OP05-022")
            .MyActiveDon(1)
            .MyDeckTop("OP09-110", "OP15-050", "OP15-051", "OP15-052", "OP15-053")
            .Build();
        var me = state.Players[0];
        var source = Card("EB02-025");
        var played = me.Deck[0];
        var firstDraw = me.Deck[1];
        var secondDraw = me.Deck[2];
        me.Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(played.Id.ToString())
            .QueueChoose(firstDraw.Id.ToString(), secondDraw.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.True(source.IsTapped);
        Assert.Equal(0, me.ActiveDonCount);
        Assert.Contains(played, me.Characters);
        Assert.True(played.IsTapped);
        Assert.Equal(state.TurnCount, played.TurnPlayed);
        Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "LookTopReveal"));
        Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "DiscardOwnChosen"));
        Assert.Contains(firstDraw, me.Trash);
        Assert.Contains(secondDraw, me.Trash);
        Assert.Equal(2, me.Deck.Count);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task G849_OP08_007_PlayedDeckAnimalResolvesOnEnterExactlyOnce()
    {
        var state = TestScene.New()
            .MyDeckTop("OP09-110", "OP15-050", "OP15-051", "OP15-052", "OP15-053")
            .Build();
        var me = state.Players[0];
        var source = Card("OP08-007");
        var played = me.Deck[0];
        var firstDraw = me.Deck[1];
        var secondDraw = me.Deck[2];
        me.Characters.Add(source);
        state.CurrentTurnPlayer = 0;
        var prompts = new MockPromptService()
            .QueueChoose(played.Id.ToString())
            .QueueChoose(firstDraw.Id.ToString(), secondDraw.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(played, me.Characters);
        Assert.True(played.IsTapped);
        Assert.Equal(state.TurnCount, played.TurnPlayed);
        Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "LookTopReveal"));
        Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "DiscardOwnChosen"));
        Assert.Contains(firstDraw, me.Trash);
        Assert.Contains(secondDraw, me.Trash);
        Assert.Equal(2, me.Deck.Count);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task G829_ST14_003_UsesCurrentCostForKoTarget()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("ST14-003");
        var ownCostSix = Card("ST13-004");
        var reducedFromSixToThree = Card("ST13-004");
        var raisedFromFiveToSix = Card("OP10-024");
        reducedFromSixToThree.CostModThisTurn = -3;
        raisedFromFiveToSix.CostModThisTurn = 1;
        me.Characters.AddRange([source, ownCostSix]);
        opponent.Characters.AddRange([reducedFromSixToThree, raisedFromFiveToSix]);
        var prompts = new MockPromptService().QueueChoose(reducedFromSixToThree.Id.ToString());

        Assert.Equal(3, state.CurrentCostOf(1, reducedFromSixToThree));
        Assert.Equal(6, state.CurrentCostOf(1, raisedFromFiveToSix));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(reducedFromSixToThree.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(raisedFromFiveToSix.Id.ToString(), targetPrompt.choices);
        Assert.Contains(reducedFromSixToThree, opponent.Trash);
        Assert.DoesNotContain(reducedFromSixToThree, opponent.Characters);
        Assert.Contains(raisedFromFiveToSix, opponent.Characters);
    }

    [Fact]
    public async Task G867_OP17_063_LaterTurnCanPayOnceButSkipsConditionalEffect()
    {
        var state = TestScene.New().MyActiveDon(1).OppCharacter("ST13-004").Build();
        var me = state.Players[0];
        var opponentTarget = Assert.Single(state.Players[1].Characters);
        var source = Card("OP17-063");
        source.TurnPlayed = state.TurnCount - 1;
        me.Characters.Add(source);
        var returnedDon = Assert.Single(me.CostArea);
        var prompts = new MockPromptService().QueueChoose(returnedDon.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(me.CostArea);
        Assert.Contains(returnedDon, me.DonDeck);
        Assert.Contains($"OP17-063-act:{source.Id}", me.TurnOnceUsed);
        Assert.Contains(source.Id, me.OncePerTurnEffectUsedCardIds);
        Assert.Contains(opponentTarget, state.Players[1].Characters);
        Assert.DoesNotContain(opponentTarget, state.Players[1].Trash);
        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", costPrompt.kind);
    }

    [Fact]
    public async Task G867_OP17_063_CancelledDonPaymentLeavesNoPartialCostOrOnceMarker()
    {
        var state = TestScene.New().MyActiveDon(1).OppCharacter("ST13-004").Build();
        var me = state.Players[0];
        var source = Card("OP17-063");
        source.TurnPlayed = state.TurnCount;
        me.Characters.Add(source);
        var retainedDon = Assert.Single(me.CostArea);
        int donDeckBefore = me.DonDeck.Count;

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService().QueueChooseEmpty());

        Assert.Equal([retainedDon], me.CostArea);
        Assert.Equal(donDeckBefore, me.DonDeck.Count);
        Assert.DoesNotContain($"OP17-063-act:{source.Id}", me.TurnOnceUsed);
        Assert.DoesNotContain(source.Id, me.OncePerTurnEffectUsedCardIds);
        Assert.Contains(state.Players[1].Characters.Single(), state.Players[1].Characters);
    }

    [Fact]
    public async Task G867_OP17_063_EntryTurnCanPayAndChooseNoTargetAsCompleteActivation()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var me = state.Players[0];
        var source = Card("OP17-063");
        source.TurnPlayed = state.TurnCount;
        me.Characters.Add(source);
        var returnedDon = Assert.Single(me.CostArea);
        var prompts = new MockPromptService().QueueChoose(returnedDon.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(me.CostArea);
        Assert.Contains(returnedDon, me.DonDeck);
        Assert.Contains($"OP17-063-act:{source.Id}", me.TurnOnceUsed);
        Assert.Contains(source.Id, me.OncePerTurnEffectUsedCardIds);
        Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompts.ChooseHistory[0].kind);
    }

    [Fact]
    public async Task G879_OP16_102_KoPlaysChosenHachinosuFromHandAmongSameNameCandidates()
    {
        var state = TestScene.New().MyDeckTop("OP15-050").Build();
        var me = state.Players[0];
        var source = Card("OP16-102");
        var fromHand = Card("OP09-099");
        var sameNameInTrash = Card("OP17-057");
        var drawn = Assert.Single(me.Deck);
        me.Trash.AddRange([source, sameNameInTrash]);
        me.Hand.Add(fromHand);
        var prompts = new MockPromptService().QueueChoose(fromHand.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        Assert.Contains(drawn, me.Hand);
        Assert.Same(fromHand, me.StageCard);
        Assert.DoesNotContain(fromHand, me.Hand);
        Assert.Contains(sameNameInTrash, me.Trash);
        var playPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(fromHand.Id.ToString(), playPrompt.choices);
        Assert.Contains(sameNameInTrash.Id.ToString(), playPrompt.choices);
    }

    [Fact]
    public async Task G879_OP16_102_KoPlaysExactChosenHachinosuFromTrashWithDuplicateInHand()
    {
        var state = TestScene.New().MyDeckTop("OP15-050").Build();
        var me = state.Players[0];
        var source = Card("OP16-102");
        var duplicateInHand = Card("OP09-099");
        var chosenFromTrash = Card("OP09-099");
        me.Trash.AddRange([source, chosenFromTrash]);
        me.Hand.Add(duplicateInHand);
        var prompts = new MockPromptService().QueueChoose(chosenFromTrash.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        Assert.Same(chosenFromTrash, me.StageCard);
        Assert.DoesNotContain(chosenFromTrash, me.Trash);
        Assert.Contains(duplicateInHand, me.Hand);
        var playPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(2, playPrompt.choices.Count);
        Assert.Contains(duplicateInHand.Id.ToString(), playPrompt.choices);
        Assert.Contains(chosenFromTrash.Id.ToString(), playPrompt.choices);
    }

    private static GameEngine CreateAttackEngine(string firstLeader, string secondLeader)
    {
        static string Deck(string leader)
            => leader + "\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));

        return new GameEngine(
            "feedback-g842-attack-tax",
            ("s0", "p0", Deck(firstLeader)),
            ("s1", "p1", Deck(secondLeader)),
            firstPlayer: 0,
            rngSeed: 20260826);
    }

    private static async Task<PendingPrompt> WaitForPrompt(GameEngine engine, string kind)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(5);
        }

        throw new TimeoutException($"等待提示 {kind} 超时");
    }
}
