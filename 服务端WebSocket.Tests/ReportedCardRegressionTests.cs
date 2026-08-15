using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class ReportedCardRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP13_117_UsesOriginalCostInsteadOfCostBuffs()
    {
        var state = TestScene.New().OppCharacter("OP07-015").Build();
        var target = state.Players[1].Characters.Single();
        target.CostModThisTurn = 6 - target.Info.Cost;
        Assert.Equal(6, state.CurrentCostOf(1, target));

        await EffectRuntime.Resolve(state, 0, new CardInstance { Info = CardDatabase.Get("OP13-117")! },
            EffectTrigger.EventMain, new MockPromptService());

        Assert.Empty(state.Players[1].Trash);
    }

    [Fact]
    public async Task OP09_081_ActivatedEffect_NullifiesOpponentEnterEffects()
    {
        var state = TestScene.New("OP09-081")
            .MyHandAdd("OP15-003")
            .OppCharacter("OP15-003")
            .Build();
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(state.Players[0].Hand.Single().Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, prompts);
        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.True(state.IsTriggerNullified(target, EffectTrigger.OnEnterField));
    }

    [Fact]
    public async Task ST17_002_ReturnsOwnCharacterAsCost_ThenAnyCurrentCostFourCharacter()
    {
        var state = TestScene.New("ST17-001")
            .MyCharacter("ST17-002")
            .MyCharacter("ST17-001")
            .OppCharacter("OP03-004")
            .Build();
        var me = state.Players[0];
        var law = me.Characters.Single(card => card.Info.Number == "ST17-002");
        var cost = me.Characters.Single(card => card.Info.Number == "ST17-001");
        var target = state.Players[1].Characters.Single();
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(cost.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, law, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(cost, me.Hand);
        Assert.Contains(target, state.Players[1].Hand);
        Assert.DoesNotContain(target, state.Players[1].Characters);
    }

    [Fact]
    public async Task NullifiedCharacter_LosesItsContinuousCostAndRushEffects()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-067")
            .OppActiveDon(6)
            .Build();
        var target = state.Players[1].Characters.Single();
        target.TurnPlayed = state.TurnCount;

        await EffectRuntime.Resolve(state, 1, target, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(target.IsEffectsNullified);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = target.Id.ToString(),
            Scope = new ContinuousScope(),
            CostDelta = 12,
            Predicate = (_, _, card) => card.Id == target.Id,
        });

        Assert.Equal(target.Info.Cost + 12, state.CurrentCostOf(1, target));
        Assert.True(GrandUMI.Game.Validation.ActionValidator.HasKeyword(state, target, "速攻"));

        AtomicOps.NullifyEffects(target, KeywordDuration.ThisTurn);

        Assert.Equal(target.Info.Cost, state.CurrentCostOf(1, target));
        Assert.False(GrandUMI.Game.Validation.ActionValidator.HasKeyword(state, target, "速攻"));
    }

    [Fact]
    public async Task OP16_119_NullifiesCostEffectBeforeChoosingFiveCostKoTarget()
    {
        var state = TestScene.New("OP16-080")
            .OppCharacter("OP03-004")
            .Build();
        var target = state.Players[1].Characters.Single();
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = target.Id.ToString(),
            Scope = new ContinuousScope(),
            CostDelta = 12,
            Predicate = (_, _, card) => card.Id == target.Id,
        });
        Assert.Equal(target.Info.Cost + 12, state.CurrentCostOf(1, target));
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, new CardInstance { Info = CardDatabase.Get("OP16-119")! },
            EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Equal(target.Info.Cost, state.CurrentCostOf(target));
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP05_098_StillTriggersWhenLastLifeIsOP06_115()
    {
        _ = TestScene.New().Build();
        string deck = "OP05-098\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("enel-last-life", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);
        var state = engine.State;
        var me = state.Players[0];
        me.Hand.Clear();
        me.Deck.Clear();
        me.LifeArea.Clear();
        me.LifeArea.Add(Card("OP06-115"));
        me.Deck.Add(Card("OP15-003"));
        me.Deck.Add(Card("OP15-004"));
        me.Hand.Add(Card("OP15-003"));
        me.Hand.Add(Card("OP15-004"));
        state.CurrentTurnPlayer = 1;

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        for (int i = 0; i < 100 && !damage.IsCompleted; i++)
        {
            if (state.PendingPrompt is { } prompt)
            {
                var choice = prompt.Kind == "LifeTrigger"
                    ? new[] { "trigger" }
                    : prompt.ValidChoices.Take(1).ToArray();
                engine.Prompts.Resolve(prompt.PromptId, choice);
            }
            await Task.Delay(10);
        }
        await damage;

        Assert.Equal(2, me.LifeArea.Count);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task OP13_064_NullifiesRogerLeaderPowerPenalty()
    {
        var state = TestScene.New("OP13-003")
            .MyCharacter("OP13-064")
            .MyActiveDon(6)
            .Build();
        var me = state.Players[0];
        var roger = me.Characters.Single(card => card.Info.Number == "OP13-064");
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnGameStart, prompts);
        Assert.Equal(5000, state.CurrentPowerOf(0, me.Leader));

        await EffectRuntime.Resolve(state, 0, roger, EffectTrigger.OnEnterField, prompts);

        Assert.True(state.IsContinuouslyNullified(me.Leader));
        Assert.Equal(9000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task OP13_063_And_OP13_072_RefreshDonWhenLeaderHasAttachedDon()
    {
        var state = TestScene.New("OP13-003").AttachDonToMyLeader(1).Build();
        var me = state.Players[0];
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });

        await EffectRuntime.Resolve(state, 0, Card("OP13-063"), EffectTrigger.OnEnterField,
            new MockPromptService());
        await EffectRuntime.Resolve(state, 0, Card("OP13-072"), EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Empty(me.DonDeck);
    }

    [Fact]
    public async Task OP16_065_ActivatedMainRestsOneDonAndAddsTwoActiveDon()
    {
        var state = TestScene.New("OP05-041").MyCharacter("OP16-065").MyActiveDon(1).Build();
        var me = state.Players[0];
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        var sakazuki = me.Characters.Single();

        await EffectRuntime.Resolve(state, 0, sakazuki, EffectTrigger.ActivatedMain,
            new MockPromptService());

        Assert.Equal(3, me.CostArea.Count);
        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Active));
        Assert.Single(me.CostArea, don => don.State == DonState.Rest);
        Assert.Empty(me.DonDeck);
    }

    [Fact]
    public async Task EB01_001_GrantsCounterToCounterlessWanoCards()
    {
        var state = TestScene.New("EB01-001").Build();
        var card = Card("EB01-002");

        Assert.Equal(1000, HandStaticCounter.Value(state, 0, card));

        AtomicOps.NullifyEffects(state.Players[0].Leader, KeywordDuration.ThisTurn);
        Assert.Equal(0, HandStaticCounter.Value(state, 0, card));
    }

    [Fact]
    public async Task EB01_001_AttackPowerLastsThroughOpponentTurn()
    {
        var state = TestScene.New("EB01-001")
            .MyCharacter("EB02-006")
            .AttachDonToMyLeader(1)
            .Build();
        var leader = state.Players[0].Leader;

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.OnAttackDeclare, new MockPromptService());

        state.CurrentTurnPlayer = 1;
        Assert.Equal(6000, state.CurrentPowerOf(0, leader));
        Assert.Single(leader.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task OP14_054_DiscardsOnlyCardsAboveFiveAtTurnEnd()
    {
        var state = TestScene.New().MyCharacter("OP14-054").Build();
        var me = state.Players[0];
        var tiger = me.Characters.Single();
        for (int i = 0; i < 7; i++) me.Hand.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 0, tiger, EffectTrigger.OnMyTurnEnd, new MockPromptService());

        Assert.Equal(5, me.Hand.Count);
        Assert.Equal(2, me.Trash.Count);
    }

    [Fact]
    public async Task OP03_030_SearchesGreenEastBlueCard()
    {
        var state = TestScene.New().MyDeckTop("OP03-023", "OP15-003").Build();
        var nami = Card("OP03-030");
        var target = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, nami, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target, state.Players[0].Hand);
    }

    [Fact]
    public async Task OP12_063_GainsPowerAndCostWithFourEventsInTrash()
    {
        var state = TestScene.New().MyCharacter("OP12-063").Build();
        var reiju = state.Players[0].Characters.Single();
        for (int i = 0; i < 4; i++) state.Players[0].Trash.Add(Card("OP15-020"));

        await EffectRuntime.Resolve(state, 0, reiju, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(reiju.Info.Power + 2000, state.CurrentPowerOf(0, reiju));
        Assert.Equal(reiju.Info.Cost + 5, state.CurrentCostOf(0, reiju));
    }

    [Fact]
    public async Task P_105_AttachesRestedDonAfterTakingLife()
    {
        var state = TestScene.New().MyCharacter("P-105").Build();
        var me = state.Players[0];
        var sabo = me.Characters.Single();
        me.LifeArea.Add(Card("OP15-003"));
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueConfirm(true).QueueOption(0).QueueChoose(sabo.Id.ToString());

        await EffectRuntime.Resolve(state, 0, sabo, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(1, me.AttachedDonCount(sabo.Id));
    }

    [Fact]
    public async Task ST14_001_RequiresAttachedDonForLeaderPowerBoost()
    {
        var state = TestScene.New("ST14-001").MyCharacter("ST14-007").Build();
        var leader = state.Players[0].Leader;
        state.Players[0].Characters.Single().CostModThisTurn = 1;

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());

        Assert.Equal(leader.Info.Power, state.CurrentPowerOf(0, leader));
    }

    [Fact]
    public async Task EB04_061_LeaderPowerLastsThroughOpponentTurn()
    {
        var state = TestScene.New().MyCharacter("EB04-061").MyHandAdd("OP15-003").Build();
        var luffy = state.Players[0].Characters.Single();
        var discard = state.Players[0].Hand.Single();
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, luffy, EffectTrigger.OnEnterField, prompts);

        state.CurrentTurnPlayer = 1;
        Assert.Equal(state.Players[0].Leader.Info.Power + 2000,
            state.CurrentPowerOf(0, state.Players[0].Leader));
        Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task EB04_007_ResolvedLeaderBuffSurvivesOpponentEffectKoUntilOpponentEnd()
    {
        var state = TestScene.New().MyCharacter("EB04-007").Build();
        var me = state.Players[0];
        var zoro = me.Characters.Single();

        await EffectRuntime.Resolve(state, 0, zoro, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(me.Leader.Info.Power + 2000, state.CurrentPowerOf(0, me.Leader));

        bool koSucceeded = await AtomicOps.KOByEffectAsync(
            state, 0, zoro, new MockPromptService(), actingSide: 1);

        Assert.True(koSucceeded);
        Assert.Contains(zoro, me.Trash);
        Assert.Equal(me.Leader.Info.Power + 2000, state.CurrentPowerOf(0, me.Leader));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(me.Leader.Info.Power + 2000, state.CurrentPowerOf(0, me.Leader));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(me.Leader.Info.Power, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task OP13_084_SetsOwnFiveElderOriginalPowerToSevenThousand()
    {
        var state = TestScene.New().MyCharacter("OP13-084").Build();
        var me = state.Players[0];
        var peter = me.Characters.Single();
        for (int i = 0; i < 10; i++) me.Trash.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 0, peter, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(7000, state.CurrentPowerOf(0, peter));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(peter.Info.Power, state.CurrentPowerOf(0, peter));
    }

    [Fact]
    public async Task OP15_022_ActivatesCharacterWhenDeckReachesZero()
    {
        var state = TestScene.New("OP15-022").MyCharacter("OP15-003")
            .MyDeckTop("OP15-003", "OP15-003", "OP15-003", "OP15-003").Build();
        var character = state.Players[0].Characters.Single();
        character.IsTapped = true;
        var prompts = new MockPromptService().QueueChoose(character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Empty(state.Players[0].Deck);
        Assert.False(character.IsTapped);
    }

    [Fact]
    public async Task OP17_118_CanPlayRocksStageFromHand()
    {
        var state = TestScene.New().MyHandAdd("OP17-057").Build();
        var rocks = Card("OP17-118");
        var stage = state.Players[0].Hand.Single();
        state.Players[0].Deck.Add(Card("OP15-003"));
        var prompts = new MockPromptService().QueueChoose(stage.Id.ToString()).QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, rocks, EffectTrigger.OnEnterField, prompts);

        Assert.Same(stage, state.Players[0].StageCard);
    }

    [Fact]
    public async Task OP06_117_CanRestEnelLeaderAsCost()
    {
        var state = TestScene.New("OP05-098").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var stage = Card("OP06-117");
        me.StageCard = stage;
        state.Players[1].Characters.Single().CostModThisTurn = -20;

        await EffectRuntime.Resolve(state, 0, stage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(me.Leader.Id.ToString()));

        Assert.True(me.Leader.IsTapped);
        Assert.True(stage.IsTapped);
        Assert.Empty(state.Players[1].Characters);
    }

    [Fact]
    public async Task OP11_092_ReturnsPlayedCharacterToDeckBottomAtTurnEnd()
    {
        var state = TestScene.New().MyCharacter("OP11-092").MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        var discard = me.Hand.Single();
        var blade = Card("EB04-044");
        me.Trash.Add(blade);
        me.Deck.Add(Card("OP15-003"));
        var prompts = new MockPromptService().QueueConfirm(true)
            .QueueChoose(discard.Id.ToString()).QueueChoose(blade.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Characters.Single(), EffectTrigger.OnEnterField, prompts);
        Assert.Contains(blade, me.Characters);

        TurnEngine.EnterEndPhase(state);

        Assert.DoesNotContain(blade, me.Characters);
        Assert.Same(blade, me.Deck[^1]);
    }

    [Fact]
    public async Task ST13_001_PutsCostCharacterFaceUpInLifeAndUsesTimedBuff()
    {
        var state = TestScene.New("ST13-001").MyCharacter("EB04-061").MyCharacter("OP15-003")
            .AttachDonToMyLeader(1).Build();
        var me = state.Players[0];
        var cost = me.Characters[0];
        var target = me.Characters[1];
        var prompts = new MockPromptService().QueueChoose(cost.Id.ToString()).QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.True(me.LifeArea.Single().IsLifeFaceUp);
        Assert.Single(target.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task ST13_015_DrawsAndTrashesTopLifeWithoutPermanentStacking()
    {
        var state = TestScene.New().MyCharacter("ST13-015").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var luffy = me.Characters.Single();
        var life = Card("OP15-004");
        me.LifeArea.Add(life);

        await EffectRuntime.Resolve(state, 0, luffy, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Contains(life, me.Trash);
        Assert.Single(me.Hand);
        Assert.Equal(0, luffy.PowerModPersistent);
        Assert.Single(luffy.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task ST13_017_ReordersAllLifeAfterCounterBuff()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = Card("OP15-003");
        var second = Card("OP15-004");
        var third = Card("OP15-005");
        me.LifeArea.AddRange([first, second, third]);
        var prompts = new MockPromptService().QueueChooseEmpty()
            .QueueChoose(third.Id.ToString()).QueueChoose(second.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("ST13-017"), EffectTrigger.EventCounter, prompts);

        Assert.Equal([third, second, first], me.LifeArea);
    }

    [Fact]
    public async Task ST26_002_CanRestOpponentDon()
    {
        var state = TestScene.New().MyActiveDon(2).OppActiveDon(1).Build();
        var opponentDon = state.Players[1].CostArea.Single();
        var myDonIds = state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray();
        var prompts = new MockPromptService().QueueChoose(myDonIds).QueueChoose(opponentDon.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("ST26-002"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(DonState.Rest, opponentDon.State);
    }

    [Fact]
    public async Task OP16_071_RequiresDiscardOnlyForOnPlayDon()
    {
        var state = TestScene.New().MyHandAdd("OP15-003").Build();
        var me = state.Players[0];
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        var card = Card("OP16-071");
        var discard = me.Hand.Single();

        await EffectRuntime.Resolve(state, 0, card, EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        await EffectRuntime.Resolve(state, 0, card, EffectTrigger.OnKO, new MockPromptService());

        Assert.Empty(me.Hand);
        Assert.Equal(2, me.CostArea.Count);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
    }

    [Fact]
    public async Task LegacySynchronousEffectKo_UsesKobyLeaveReplacement()
    {
        var state = TestScene.New(oppLeaderNumber: "OP11-001").OppCharacter("OP16-064").Build();
        var attacker = Card("OP02-004");
        state.Players[0].Characters.Add(attacker);
        for (int i = 0; i < 2; i++)
            state.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = attacker.Id });
        var victim = state.Players[1].Characters.Single();
        var trash = new[] { Card("OP15-003"), Card("OP15-004"), Card("OP15-005") };
        state.Players[1].Trash.AddRange(trash);
        var prompts = new MockPromptService().QueueChoose(victim.Id.ToString()).QueueConfirm(true)
            .QueueChoose(trash.Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, attacker, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(victim, state.Players[1].Characters);
        Assert.Empty(state.Players[1].Trash);
        Assert.Equal(3, state.Players[1].Deck.Count);
    }

    [Fact]
    public async Task OP07_056_BounceCostNotifiesBuggyLeaveWatcher()
    {
        var state = TestScene.New("OP16-041").MyCharacter("OP16-042").MyHandAdd("OP16-042")
            .AttachDonToMyLeader(1).Build();
        var me = state.Players[0];
        var bounce = me.Characters.Single();
        var prisoner = me.Hand.Single();
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(bounce.Id.ToString())
            .QueueChooseEmpty().QueueChoose(prisoner.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP07-056"), EffectTrigger.EventCounter, prompts);

        Assert.Contains(bounce, me.Hand);
        Assert.Contains(prisoner, me.Characters);
    }

    [Fact]
    public async Task OP15_001_ActivatedMainDoesNotRequireLeaderDon()
    {
        var state = TestScene.New("OP15-001").OppCharacter("OP15-003").Build();
        var target = state.Players[1].Characters.Single();
        for (int i = 0; i < 2; i++)
            state.Players[1].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = target.Id });

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task OP15_023_CanPreventLeaderAndDonFromResetting()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        opponent.Leader.IsTapped = true;
        var don = new DonCard { State = DonState.Rest };
        opponent.CostArea.Add(don);
        var prompts = new MockPromptService().QueueChoose(opponent.Leader.Id.ToString(), don.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-023"), EffectTrigger.OnKO, prompts);
        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);

        Assert.True(opponent.Leader.IsTapped);
        Assert.Equal(DonState.Rest, don.State);
    }

    [Fact]
    public void OP01_121_IsAlsoNamedKozukiOden()
    {
        Assert.True(Card("OP01-121").MatchesName("光月御殿"));
    }

    [Fact]
    public async Task ST21_001_CannotActivateWithoutAttachedDon()
    {
        var state = TestScene.New("ST21-001").MyCharacter("OP15-003").Build();
        var me = state.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Equal(0, me.AttachedDonCount(me.Characters.Single().Id));
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task OP09_001_DecliningDoesNotConsumeOncePerTurn()
    {
        var state = TestScene.New("OP09-001").Build();
        var leader = state.Players[0].Leader;
        var target = state.Players[1].Leader;

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChooseEmpty());
        Assert.Empty(state.Players[0].TurnOnceUsed);

        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.OnOppAttackDeclare,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Equal(-1000, target.PowerModThisTurn);
        Assert.Single(state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task OP13_016_SearchesWithEligibleLeader()
    {
        var state = TestScene.New("ST13-001").MyDeckTop("OP15-003").Build();
        var target = state.Players[0].Deck.Single();

        await EffectRuntime.Resolve(state, 0, Card("OP13-016"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Contains(target, state.Players[0].Hand);
    }

    [Fact]
    public async Task P_088_LifeTriggerPlaysItselfFromTrash()
    {
        var state = TestScene.New("OP01-002").Build();
        var law = Card("P-088");
        state.Players[0].Trash.Add(law);

        await EffectRuntime.Resolve(state, 0, law, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Contains(law, state.Players[0].Characters);
        Assert.DoesNotContain(law, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP17_110_KeepsRushAfterOverflowReplacement()
    {
        var state = TestScene.New()
            .MyCharacter("OP17-110")
            .MyCharacter("OP15-003")
            .MyCharacter("OP15-004")
            .MyCharacter("OP15-005")
            .MyCharacter("OP15-006")
            .MyHandAdd("OP17-109")
            .Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var overflowVictim = me.Characters[1];
        var summoned = me.Hand.Single();
        var prompts = new MockPromptService()
            .QueueChoose(summoned.Id.ToString())
            .QueueChoose(overflowVictim.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(source, me.Characters);
        Assert.Contains(summoned, me.Characters);
        Assert.Contains(overflowVictim, me.Trash);
        Assert.True(GrandUMI.Game.Validation.ActionValidator.HasKeyword(state, source, "速攻"));
    }
}
