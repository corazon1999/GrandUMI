using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class OP17EffectTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public async Task OP17_095_ReturnsThreeTrashOnceToProtectAllSimultaneousEffectKOs()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var guard = Card("OP17-095");
        var first = Card("ST30-006");
        var second = Card("ST30-007");
        var trash = new[] { Card("ST30-002"), Card("ST30-003"), Card("ST30-004") };
        me.Characters.AddRange([guard, first, second]);
        me.Trash.AddRange(trash);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(trash.Select(card => card.Id.ToString()).ToArray());
        state.KOReason = "effect";
        state.KOActingSide = 1;

        var koCount = await BattleEngine.KOCardsSimultaneouslyAsync(
            state, 0, [first, second], prompts);

        Assert.Equal(0, koCount);
        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.Empty(me.Trash);
        Assert.Equal(trash, me.Deck);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP17_036_EventMain_CannotUseCharactersToCompleteDonCost()
    {
        var state = TestScene.New("OP17-001")
            .MyActiveDon(5)
            .MyCharacter("OP17-005")
            .Build();
        var character = Assert.Single(state.Players[0].Characters);
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, Card("OP17-036"),
            EffectTrigger.EventMain, prompts);

        Assert.Equal(5, state.Players[0].ActiveDonCount);
        Assert.False(character.IsTapped);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP17_036_EventMain_RestsOnlySixActiveDon()
    {
        var state = TestScene.New("OP17-001")
            .MyActiveDon(6)
            .MyCharacter("OP17-005")
            .Build();
        var me = state.Players[0];
        var character = Assert.Single(me.Characters);
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, Card("OP17-036"),
            EffectTrigger.EventMain, prompts);

        Assert.Equal(0, me.ActiveDonCount);
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.False(me.Leader.IsTapped);
        Assert.False(character.IsTapped);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Theory]
    [InlineData("OP08-051", true)]
    [InlineData("OP07-049", false)]
    public async Task OP17_039_DrawsOnlyWhenRevealedTypeContainsRocksPirates(
        string revealedNumber, bool shouldDraw)
    {
        var state = TestScene.New("OP17-039").Build();
        var me = state.Players[0];
        var discard = Card("OP17-040");
        var revealed = Card(revealedNumber);
        var next = Card("OP17-041");
        me.Hand.Add(discard);
        me.Deck.AddRange([revealed, next]);
        var prompts = new MockPromptService().QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader,
            EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(discard, me.Trash);
        if (shouldDraw)
        {
            Assert.Empty(me.Deck);
            Assert.Equal(2, me.Hand.Count);
            Assert.Contains(revealed, me.Hand);
            Assert.Contains(next, me.Hand);
        }
        else
        {
            Assert.Empty(me.Hand);
            Assert.Equal(2, me.Deck.Count);
            Assert.Same(revealed, me.Deck[0]);
        }
    }

    [Fact]
    public async Task OP17_042_OnPlay_CanDeclineWithoutRevealingHandOrReducingPower()
    {
        var state = TestScene.New("OP17-039").OppCharacter("OP17-011").Build();
        var source = Card("OP17-042");
        var target = Assert.Single(state.Players[1].Characters);
        state.Players[0].Characters.Add(source);
        state.Players[0].Hand.AddRange([
            Card("OP17-040"),
            Card("OP17-041"),
            Card("OP17-044"),
        ]);
        var prompts = new MockPromptService().QueueConfirm(false);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(0, target.PowerModThisTurn);
        Assert.Equal(3, state.Players[0].Hand.Count);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP17_099_FirstChoice_DiscardsOwnersHandAndAddsDeckTopToLife()
    {
        var state = TestScene.New("OP17-099").MyDeckTop("OP17-100").Build();
        var activationCost = Card("OP17-101");
        var optionDiscard = Card("OP17-102");
        var opponentCard = Card("OP17-103");
        var lifeCard = Assert.Single(state.Players[0].Deck);
        state.Players[0].Hand.AddRange([activationCost, optionDiscard]);
        state.Players[1].Hand.Add(opponentCard);
        int lifeBefore = state.Players[0].LifeArea.Count;
        var prompts = new MockPromptService()
            .QueueChoose(activationCost.Id.ToString())
            .QueueOption(0)
            .QueueChoose(optionDiscard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader,
            EffectTrigger.OnAttackDeclare, prompts);

        Assert.Empty(state.Players[0].Hand);
        Assert.Contains(activationCost, state.Players[0].Trash);
        Assert.Contains(optionDiscard, state.Players[0].Trash);
        Assert.Equal(lifeBefore + 1, state.Players[0].LifeArea.Count);
        Assert.Contains(lifeCard, state.Players[0].LifeArea);
        Assert.Contains(opponentCard, state.Players[1].Hand);
        Assert.Empty(state.Players[1].Trash);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.All(prompts.ChooseHistory, prompt => Assert.Equal("OwnHandDiscard", prompt.kind));
    }

    [Fact]
    public async Task OP17_099_SecondChoice_RandomlyDiscardsOpponentWithoutCardPrompt()
    {
        var state = TestScene.New("OP17-099").Build();
        var activationCost = Card("OP17-101");
        var remainingOwnerCard = Card("OP17-102");
        state.Players[0].Hand.AddRange([activationCost, remainingOwnerCard]);
        state.Players[1].Hand.AddRange([Card("OP17-103"), Card("OP17-104")]);
        var prompts = new MockPromptService()
            .QueueChoose(activationCost.Id.ToString())
            .QueueOption(1);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader,
            EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(remainingOwnerCard, state.Players[0].Hand);
        Assert.Contains(activationCost, state.Players[0].Trash);
        Assert.Single(state.Players[1].Hand);
        Assert.Single(state.Players[1].Trash);
        var discardPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnHandDiscard", discardPrompt.kind);
        Assert.Equal("选择丢弃1张手牌", discardPrompt.text);
    }

    [Fact]
    public async Task OP17_091_MakesOpponentChooseWhichHandCardToDiscard()
    {
        var state = TestScene.New("OP17-039").Build();
        var brook = Card("OP17-091");
        var chosen = Card("OP17-044");
        var kept = Card("OP17-085");
        var highCost = Card("OP17-118");
        highCost.CostModPersistent = 2;
        state.Players[0].Characters.Add(brook);
        state.Players[1].Characters.Add(highCost);
        state.Players[1].Hand.AddRange([chosen, kept]);
        var prompts = new MockPromptService().QueueChoose(chosen.Id.ToString());

        await EffectRuntime.Resolve(state, 0, brook, EffectTrigger.OnEnterField, prompts);

        Assert.DoesNotContain(chosen, state.Players[1].Hand);
        Assert.Contains(chosen, state.Players[1].Trash);
        Assert.Contains(kept, state.Players[1].Hand);
        var discard = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnHandDiscard", discard.kind);
    }

    [Fact]
    public async Task OP17_106_OnPlay_MakesOpponentChooseWhichHandCardToDiscard()
    {
        var state = TestScene.New()
            .MyActiveDon(2)
            .MyDeckTop("OP17-107")
            .Build();
        var smoothie = Card("OP17-106");
        var kept = Card("OP17-044");
        var chosen = Card("OP17-085");
        state.Players[0].Characters.Add(smoothie);
        state.Players[1].Hand.AddRange([kept, chosen]);
        int lifeBefore = state.Players[0].LifeArea.Count;
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(chosen.Id.ToString());

        await EffectRuntime.Resolve(state, 0, smoothie, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(0, state.Players[0].ActiveDonCount);
        Assert.Equal(lifeBefore + 1, state.Players[0].LifeArea.Count);
        Assert.Contains(kept, state.Players[1].Hand);
        Assert.DoesNotContain(chosen, state.Players[1].Hand);
        Assert.Contains(chosen, state.Players[1].Trash);
        var discard = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnHandDiscard", discard.kind);
        Assert.Equal(new[] { kept.Id.ToString(), chosen.Id.ToString() }, discard.choices);
    }

    private static string LegalOp17Deck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP17")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            lines.Add(card.Number);
        }
        return string.Join('\n', lines);
    }

    [Fact]
    public void OP17_063_And_118_ExposeDynamicHandCounters()
    {
        var auraState = TestScene.New("OP17-039").Build();
        var ged = Card("OP17-063");
        var noCounter = Card("OP17-044");
        auraState.Players[0].Characters.Add(ged);
        auraState.Players[0].Hand.Add(noCounter);

        Assert.Equal(1000, HandStaticCounter.Value(auraState, 0, noCounter));

        var rocksState = TestScene.New("OP17-039").Build();
        var rocks = Card("OP17-118");
        rocksState.Players[0].Hand.Add(rocks);
        rocksState.Players[0].Characters.Add(Card("OP17-044"));
        Assert.Equal(2000, HandStaticCounter.Value(rocksState, 0, rocks));

        rocksState.Players[0].Characters.Add(Card("OP17-085"));
        Assert.Equal(0, HandStaticCounter.Value(rocksState, 0, rocks));
    }

    [Fact]
    public void OP17_005_HandCostDropsByFour_WhenOpponentHas10000PowerCharacter()
    {
        var state = TestScene.New("OP17-001").Build();
        var whitebeard = Card("OP17-005");
        state.Players[0].Hand.Add(whitebeard);
        state.Players[1].Characters.Add(Card("OP17-005"));

        Assert.Equal(6, state.HandPlayCost(0, whitebeard));
    }

    [Fact]
    public async Task OP17_005_OriginalPowerOverride_ExpiresAtOpponentEndPhase()
    {
        var state = TestScene.New("OP17-001").Build();
        var whitebeard = Card("OP17-005");
        state.Players[0].Characters.Add(whitebeard);

        await EffectRuntime.Resolve(state, 0, whitebeard, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(8000, state.CurrentPowerOf(0, state.Players[0].Leader));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(8000, state.CurrentPowerOf(0, state.Players[0].Leader));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5000, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public void OP17_044_RestedJohnCaptain_ForcesAllAttackTargetsToJohn()
    {
        var state = TestScene.New("OP17-039").Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 3;
        var john = Card("OP17-044");
        var other = Card("OP17-040");
        john.IsTapped = true;
        other.IsTapped = true;
        state.Players[0].Characters.Add(john);
        state.Players[0].Characters.Add(other);
        var attacker = state.Players[1].Leader;

        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, true, null).Ok);
        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, false, other.Id).Ok);
        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, false, john.Id).Ok);
    }

    [Fact]
    public async Task OP17_079_GrantsBlockerToCharactersWhoseCurrentCostIsAtLeast12()
    {
        var state = TestScene.New("OP17-079").Build();
        var dorry = Card("OP17-085");
        state.Players[0].Characters.Add(dorry);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, dorry, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(state.CurrentCostOf(0, dorry) >= 12);
        Assert.True(ActionValidator.HasKeyword(state, dorry, "阻挡者"));
    }

    [Fact]
    public async Task OP17_107_LifeTrigger_PlaysItselfFromTrash()
    {
        var state = TestScene.New().Build();
        var daifuku = Card("OP17-107");
        state.Players[0].Trash.Add(daifuku);

        await EffectRuntime.Resolve(state, 0, daifuku, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Contains(daifuku, state.Players[0].Characters);
        Assert.DoesNotContain(daifuku, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP17_040_LeaderBattleWatcher_DiscardsOneAndAdds3000ForBattle()
    {
        var state = TestScene.New("OP17-039").Build();
        var watcher = Card("OP17-040");
        state.Players[0].Characters.Add(watcher);
        state.Players[0].Hand.Add(Card("OP17-044"));
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = state.Players[0].Leader.Id,
            DefenderPlayerIndex = 1,
            TargetIsLeader = true,
        };

        await EffectRuntime.Resolve(state, 0, watcher, EffectTrigger.OnLeaderBattle, new MockPromptService());

        Assert.Equal(3000, state.Players[0].Leader.PowerModThisBattle);
        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[0].Trash);
    }

    [Fact]
    public void OP17_040_HasLeaderBattleListener_And_OP17_024_DoesNot()
    {
        _ = TestScene.New().Build();

        Assert.True(EffectRuntime.HasEffectForTrigger(Card("OP17-040"), EffectTrigger.OnLeaderBattle));
        Assert.False(EffectRuntime.HasEffectForTrigger(Card("OP17-024"), EffectTrigger.OnLeaderBattle));
    }

    [Fact]
    public void DebugAddLife_PutsSpecifiedCardOnRequestedLifeTop()
    {
        _ = TestScene.New().Build();
        var deck = LegalOp17Deck("OP17-099");
        var engine = new GameEngine("op17-debug-life", ("s0", "alice", deck), ("s1", "bob", deck), 0, 17);
        int before = engine.State.Players[1].LifeArea.Count;

        engine.HandleAction(0, "DebugAddLife", JsonSerializer.SerializeToElement(new
        {
            cardNumber = "OP17-117",
            target = "opponent",
        }));

        Assert.Equal(before + 1, engine.State.Players[1].LifeArea.Count);
        Assert.Equal("OP17-117", engine.State.Players[1].LifeArea[0].Info.Number);
        Assert.False(engine.State.Players[1].LifeArea[0].IsLifeFaceUp);
    }

    [Fact]
    public async Task DebugRunOP17Coverage_BroadcastsCurrentLeaderColorReport()
    {
        _ = TestScene.New().Build();
        var deck = LegalOp17Deck("OP17-001");
        var engine = new GameEngine("op17-coverage", ("s0", "alice", deck), ("s1", "bob", deck), 0, 17);
        var messages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (player, payload) =>
        {
            if (player == 0) messages.Enqueue(JsonSerializer.Serialize(payload));
        };

        engine.HandleAction(0, "DebugRunOP17Coverage", JsonSerializer.SerializeToElement(new { }));
        await engine.WaitSettledAsync(60_000);

        var parsed = messages.Select(message => JsonDocument.Parse(message)).ToList();
        Assert.Contains(parsed, document => document.RootElement.GetProperty("lastAction").GetString() == "DebugOP17CoverageStarted");
        var resultMessage = Assert.Single(parsed.Where(document =>
            document.RootElement.GetProperty("lastAction").GetString() == "DebugOP17CoverageResult"));
        using var resultPayload = JsonDocument.Parse(resultMessage.RootElement.GetProperty("actionPayload").GetString()!);
        Assert.Equal("红", resultPayload.RootElement.GetProperty("color").GetString());
        Assert.Equal(19, resultPayload.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, resultPayload.RootElement.GetProperty("failed").GetInt32());
    }

    [Theory]
    [InlineData("OP17-032", EffectTrigger.OnEnterField, 3)]
    [InlineData("OP17-033", EffectTrigger.OnEnterField, 3)]
    [InlineData("OP17-037", EffectTrigger.EventMain, 5)]
    public async Task OP17_SearchTop_IncludesSubgroupTrait_AndRevealsOnlyAddedCard(
        string sourceNumber, EffectTrigger trigger, int checkedCount)
    {
        _ = TestScene.New().Build();
        var deck = LegalOp17Deck("OP17-039");
        var engine = new GameEngine($"op17-search-reveal-{sourceNumber}", ("s0", "alice", deck), ("s1", "bob", deck), 0, 17);
        var messages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 0) messages.Enqueue(JsonSerializer.Serialize(payload));
        };

        var eligibleInfo = CardDatabase.Get("OP17-021")!;
        Assert.False(eligibleInfo.HasKeyword("红发海盗团"));
        Assert.True(eligibleInfo.HasKeywordContaining("红发海盗团"));
        var ineligibleInfos = CardDatabase.GetBySet("OP17")
            .Where(c => c.Kind != CardKind.Leader && !c.HasKeywordContaining("红发海盗团"))
            .Take(checkedCount - 1)
            .ToList();
        Assert.Equal(checkedCount - 1, ineligibleInfos.Count);

        var selected = new CardInstance { Info = eligibleInfo };
        var checkedCards = new List<CardInstance> { selected };
        checkedCards.AddRange(ineligibleInfos.Select(info => new CardInstance { Info = info }));
        var player = engine.State.Players[0];
        player.Deck.Clear();
        player.Deck.AddRange(checkedCards);

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, Card(sourceNumber), trigger, engine.Prompts);

        for (int i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Single(prompt.ValidChoices);
        Assert.Equal(selected.Id.ToString(), prompt.ValidChoices[0]);
        using (var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(prompt.Extra["choiceCards"])))
            Assert.Equal(checkedCount, choiceCards.RootElement.GetArrayLength());

        engine.Prompts.Resolve(prompt.PromptId, new[] { selected.Id.ToString() });
        await resolveTask;

        Assert.Contains(selected, player.Hand);
        var revealMessages = messages
            .Select(message => JsonDocument.Parse(message))
            .Where(document => document.RootElement.GetProperty("lastAction").GetString() == "RevealCards")
            .ToList();
        var revealMessage = Assert.Single(revealMessages);
        var revealedNumbers = revealMessage.RootElement
            .GetProperty("reveal")
            .GetProperty("cardNumbers")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();
        Assert.Equal(new[] { selected.Info.Number }, revealedNumbers);

        foreach (var document in revealMessages) document.Dispose();
    }

    [Fact]
    public async Task OP17_032_OnPlay_ShowsLookTopPrompt_WhenNoEligibleCardIsFound()
    {
        _ = TestScene.New().Build();
        var deck = LegalOp17Deck("OP17-039");
        var engine = new GameEngine("op17-032-empty-search", ("s0", "alice", deck), ("s1", "bob", deck), 0, 17);
        var player = engine.State.Players[0];
        var looked = CardDatabase.GetBySet("OP17")
            .Where(card => card.Kind != CardKind.Leader && !card.HasKeywordContaining("红发海盗团"))
            .Take(3)
            .Select(info => new CardInstance { Info = info })
            .ToList();
        Assert.Equal(3, looked.Count);
        player.Deck.Clear();
        player.Deck.AddRange(looked);

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, Card("OP17-032"), EffectTrigger.OnEnterField, engine.Prompts);

        for (int i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal("LookTop", prompt.Kind);
        Assert.Empty(prompt.ValidChoices);
        using (var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(prompt.Extra["choiceCards"])))
            Assert.Equal(3, choiceCards.RootElement.GetArrayLength());

        engine.Prompts.Resolve(prompt.PromptId, Array.Empty<string>());
        await resolveTask;

        Assert.Equal(looked, player.Deck);
    }

    [Fact]
    public void OP17_NewCards_HaveExpectedPrintedValues()
    {
        _ = TestScene.New().Build();

        Assert.Equal(119, CardDatabase.GetBySet("OP17").Count);
        Assert.Equal("哈尔塔", CardDatabase.Get("OP17-009")!.Name);
        Assert.Equal("杰克", CardDatabase.Get("OP17-069")!.Name);
        Assert.Equal("X·德雷克", CardDatabase.Get("OP17-075")!.Name);
        Assert.Equal("海尔丁", CardDatabase.Get("OP17-088")!.Name);
        Assert.Equal(1000, CardDatabase.Get("OP17-011")!.Counter);
        Assert.Equal(1000, CardDatabase.Get("OP17-014")!.Counter);
        Assert.Equal(2000, CardDatabase.Get("OP17-023")!.Counter);
        Assert.Equal(2000, CardDatabase.Get("OP17-100")!.Counter);
        Assert.Equal(6000, CardDatabase.Get("OP17-011")!.Power);
        Assert.Equal(10000, CardDatabase.Get("OP17-047")!.Power);
        Assert.Equal(8000, CardDatabase.Get("OP17-100")!.Power);
        Assert.Equal("UC", CardDatabase.Get("OP17-016")!.Rarity);
        Assert.Equal("UC", CardDatabase.Get("OP17-021")!.Rarity);
        Assert.Equal("特", CardDatabase.Get("OP17-023")!.Property);
        Assert.Equal("斩", CardDatabase.Get("OP17-047")!.Property);
        Assert.Equal("R", CardDatabase.Get("OP17-076")!.Rarity);
        Assert.Equal("UC", CardDatabase.Get("OP17-102")!.Rarity);
        Assert.Equal("UC", CardDatabase.Get("OP17-107")!.Rarity);
        Assert.Null(ScriptedEffectRegistry.TryGet("OP17-070"));
        Assert.Null(ScriptedEffectRegistry.TryGet("OP17-088"));
        Assert.Null(ScriptedEffectRegistry.TryGet("OP17-100"));
    }

    [Fact]
    public async Task OP17_009_OnPlay_KOsWeakCharacter_AndGainsPowerOnOpponentTurn()
    {
        var state = TestScene.New().Build();
        var haruta = Card("OP17-009");
        var target = Card("OP17-012");
        state.Players[0].Characters.Add(haruta);
        state.Players[1].Characters.Add(target);

        await EffectRuntime.Resolve(state, 0, haruta, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
        Assert.Equal(5000, state.CurrentPowerOf(0, haruta));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(8000, state.CurrentPowerOf(0, haruta));
    }

    [Fact]
    public async Task OP17_010_ActivatedMain_GrantsBlockerAndPowerOnlyOnce()
    {
        var state = TestScene.New().Build();
        var fossa = Card("OP17-010");
        state.Players[0].Characters.Add(fossa);
        state.Players[1].Characters.Add(Card("OP17-069"));

        await EffectRuntime.Resolve(state, 0, fossa, EffectTrigger.ActivatedMain, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, fossa, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(ActionValidator.HasKeyword(state, fossa, "阻挡者"));
        Assert.Equal(5000, state.CurrentPowerOf(0, fossa));
        Assert.Single(fossa.GainedKeywords);
        Assert.Single(fossa.PowerModsUntilOppEnd);
        Assert.Single(state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task OP17_011_AttachedTwoDon_WhenAttacking_ReducesOpponentPower()
    {
        var state = TestScene.New().Build();
        var blamenco = Card("OP17-011");
        var target = Card("OP17-023");
        state.Players[0].Characters.Add(blamenco);
        state.Players[1].Characters.Add(target);
        for (int i = 0; i < 2; i++)
            state.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = blamenco.Id });

        await EffectRuntime.Resolve(state, 0, blamenco, EffectTrigger.OnAttackDeclare, new MockPromptService());

        Assert.Equal(-4000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task OP17_014_HandlesOnPlayKO_AndOpponentAttackCost()
    {
        var onPlay = TestScene.New().Build();
        var whiteyBay = Card("OP17-014");
        var weakTarget = Card("OP17-012");
        onPlay.Players[0].Characters.Add(whiteyBay);
        onPlay.Players[1].Characters.Add(weakTarget);

        await EffectRuntime.Resolve(onPlay, 0, whiteyBay, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.DoesNotContain(weakTarget, onPlay.Players[1].Characters);
        Assert.Contains(weakTarget, onPlay.Players[1].Trash);

        var defense = TestScene.New().Build();
        whiteyBay = Card("OP17-014");
        defense.Players[0].Characters.Add(whiteyBay);

        await EffectRuntime.Resolve(defense, 0, whiteyBay, EffectTrigger.OnOppAttackDeclare, new MockPromptService());
        Assert.DoesNotContain(whiteyBay, defense.Players[0].Characters);
        Assert.Contains(whiteyBay, defense.Players[0].Trash);
        Assert.Equal(1000, defense.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP17_018_HandlesMainAndCounterModes()
    {
        var main = TestScene.New().MyActiveDon(2).Build();
        var stage = Card("OP16-021");
        main.Players[1].StageCard = stage;

        await EffectRuntime.Resolve(main, 0, Card("OP17-018"), EffectTrigger.EventMain, new MockPromptService());
        Assert.Equal(2, main.Players[0].RestDonCount);
        Assert.Null(main.Players[1].StageCard);
        Assert.Contains(stage, main.Players[1].Trash);

        var counter = TestScene.New().Build();
        counter.Players[0].Characters.Add(Card("OP17-005"));
        counter.Players[0].Characters.Add(Card("OP17-022"));

        await EffectRuntime.Resolve(counter, 0, Card("OP17-018"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.Equal(4000, counter.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP17_018_Counter_UsesCurrentPowerForCondition()
    {
        var boosted = TestScene.New().Build();
        var first = Card("OP17-003");
        var second = Card("OP17-003");
        first.PowerModThisTurn = 2000;
        second.PowerModThisTurn = 2000;
        boosted.Players[0].Characters.Add(first);
        boosted.Players[0].Characters.Add(second);

        await EffectRuntime.Resolve(boosted, 0, Card("OP17-018"), EffectTrigger.EventCounter, new MockPromptService());

        Assert.Equal(4000, boosted.Players[0].Leader.PowerModThisBattle);

        var reduced = TestScene.New().Build();
        first = Card("OP17-005");
        second = Card("OP17-022");
        first.PowerModThisTurn = -5000;
        reduced.Players[0].Characters.Add(first);
        reduced.Players[0].Characters.Add(second);

        await EffectRuntime.Resolve(reduced, 0, Card("OP17-018"), EffectTrigger.EventCounter, new MockPromptService());

        Assert.Equal(0, reduced.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP17_017_Counter_BoostsWhitebeardCardAndReducesOpponentPower()
    {
        var state = TestScene.New().Build();
        var own = Card("OP17-011");
        var opponent = Card("OP17-023");
        state.Players[0].Characters.Add(own);
        state.Players[1].Characters.Add(opponent);
        var prompts = new MockPromptService()
            .QueueChoose(own.Id.ToString())
            .QueueChoose(opponent.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-017"), EffectTrigger.EventCounter, prompts);

        Assert.Equal(2000, own.PowerModThisBattle);
        Assert.Equal(-2000, opponent.PowerModThisTurn);
    }

    [Fact]
    public async Task OP17_021_RestsOwnCardToProtectSubgroupCharacterFromOpponentEffectKO()
    {
        var state = TestScene.New().Build();
        var ouri = Card("OP17-021");
        state.Players[0].Characters.Add(ouri);
        var prompts = new MockPromptService().QueueChoose(ouri.Id.ToString());

        bool wasKOd = await AtomicOps.KOByEffectAsync(state, 0, ouri, prompts, actingSide: 1);

        Assert.False(wasKOd);
        Assert.Contains(ouri, state.Players[0].Characters);
        Assert.DoesNotContain(ouri, state.Players[0].Trash);
        Assert.True(ouri.IsTapped);
        Assert.Contains(prompts.ConfirmHistory, text => text.Contains("《红发海盗团》角色不离场"));
    }

    [Fact]
    public async Task OP17_023_RestsToProtectEligibleAllyAndItselfFromKO()
    {
        var allyState = TestScene.New().Build();
        var nami = Card("OP17-023");
        var ally = Card("OP17-086");
        allyState.Players[0].Characters.Add(nami);
        allyState.Players[0].Characters.Add(ally);

        await AtomicOps.KOByEffectAsync(allyState, 0, ally, new MockPromptService(), actingSide: 1);
        Assert.Contains(ally, allyState.Players[0].Characters);
        Assert.True(nami.IsTapped);

        var selfState = TestScene.New().Build();
        nami = Card("OP17-023");
        selfState.Players[0].Characters.Add(nami);

        await BattleEngine.KOCardAsync(selfState, 0, nami, new MockPromptService());
        Assert.Contains(nami, selfState.Players[0].Characters);
        Assert.True(nami.IsTapped);
    }

    [Fact]
    public async Task OP17_047_EndTurn_ReturnsOpponentChosenHandToDeckBottom()
    {
        var state = TestScene.New().Build();
        var shiki = Card("OP17-047");
        state.Players[0].Characters.Add(shiki);
        state.Players[0].Hand.Add(Card("OP17-023"));
        var returned = Card("OP17-011");
        state.Players[1].Hand.Add(returned);

        await EffectRuntime.Resolve(state, 0, shiki, EffectTrigger.OnMyTurnEnd, new MockPromptService());

        Assert.DoesNotContain(returned, state.Players[1].Hand);
        Assert.Same(returned, state.Players[1].Deck[^1]);
    }

    [Fact]
    public async Task OP17_048_AttackTrigger_AcceptsFormerRocksPiratesHandCard()
    {
        var state = TestScene.New().OppCharacter("OP17-011").Build();
        var source = Card("OP17-048");
        var formerRocksPirate = Card("OP08-051");
        var unrelated = Card("OP17-023");
        var target = state.Players[1].Characters[0];
        state.Players[0].Characters.Add(source);
        state.Players[0].Hand.AddRange([formerRocksPirate, unrelated]);
        var prompts = new MockPromptService()
            .QueueChoose(formerRocksPirate.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnOppAttackDeclare, prompts);

        Assert.DoesNotContain(formerRocksPirate, state.Players[0].Hand);
        Assert.Contains(formerRocksPirate, state.Players[0].Trash);
        Assert.Contains(unrelated, state.Players[0].Hand);
        Assert.Equal(-3000, target.PowerModThisTurn);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Equal(
            new[] { formerRocksPirate.Id.ToString() },
            prompts.ChooseHistory[0].choices);
    }

    [Fact]
    public async Task OP17_058_AttackTrigger_AllowsCancellingDonMinusWithActiveDon()
    {
        var state = TestScene.New("OP17-058").MyActiveDon(1).OppCharacter("OP17-011").Build();
        var target = state.Players[1].Characters[0];
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnOppAttackDeclare, prompts);

        Assert.Single(state.Players[0].CostArea);
        Assert.Empty(state.Players[0].DonDeck);
        Assert.Equal(0, target.PowerModThisTurn);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompt.kind);
        Assert.Single(prompt.choices);
        Assert.Equal(state.Players[0].CostArea[0].Id.ToString(), prompt.choices[0]);
        Assert.Equal(0, prompt.min);
        Assert.Equal(1, prompt.max);
        Assert.True(Assert.IsType<bool>(prompt.extra!["canCancel"]));
    }

    [Fact]
    public async Task OP17_058_DonMinusPrompt_IncludesActiveRestAndAttachedDon()
    {
        var state = TestScene.New("OP17-058")
            .MyActiveDon(1)
            .MyCharacter("OP17-011")
            .OppCharacter("OP17-011")
            .Build();
        var me = state.Players[0];
        var ownCharacter = Assert.Single(me.Characters);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = ownCharacter.Id });
        var expectedIds = me.CostArea.Select(d => d.Id.ToString()).ToHashSet();
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompt.kind);
        Assert.Equal(expectedIds, prompt.choices.ToHashSet());
        var donChoices = JsonSerializer.SerializeToElement(prompt.extra!["donChoices"])
            .EnumerateArray()
            .ToList();
        var leaderDon = Assert.Single(donChoices.Where(choice =>
            choice.GetProperty("attachedToCardId").GetString() == me.Leader.Id.ToString()));
        Assert.Equal(me.Leader.Info.Number, leaderDon.GetProperty("attachedToNumber").GetString());
        Assert.Equal(me.Leader.Info.Name, leaderDon.GetProperty("attachedToName").GetString());
        var characterDon = Assert.Single(donChoices.Where(choice =>
            choice.GetProperty("attachedToCardId").GetString() == ownCharacter.Id.ToString()));
        Assert.Equal(ownCharacter.Info.Number, characterDon.GetProperty("attachedToNumber").GetString());
        Assert.Equal(ownCharacter.Info.Name, characterDon.GetProperty("attachedToName").GetString());
        Assert.Equal(4, me.CostArea.Count);
        Assert.Empty(me.DonDeck);
    }

    [Fact]
    public async Task OP17_058_AttackTrigger_CanPayWithActiveDonAndReduceOpponentPower()
    {
        var state = TestScene.New("OP17-058").MyActiveDon(1).OppCharacter("OP17-011").Build();
        var donId = state.Players[0].CostArea[0].Id.ToString();
        var target = state.Players[1].Characters[0];
        var prompts = new MockPromptService()
            .QueueChoose(donId)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Equal(-2000, target.PowerModThisTurn);
        Assert.Contains(
            $"OP17-058-battle:{state.Players[0].Leader.Id}",
            state.Players[0].TurnOnceUsed);
    }

    [Fact]
    public async Task OP17_067_OnPlay_ReturnsDonAndRestsActiveOpponentWhenOwnCostTenExists()
    {
        var state = TestScene.New("OP17-058")
            .MyActiveDon(1)
            .MyCharacter("OP17-063")
            .OppCharacter("OP17-011")
            .Build();
        var source = Card("OP17-067");
        var target = state.Players[1].Characters[0];
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].CostArea[0].Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task OP17_068_OnAttack_DiscardsTwoAndAddsTwoRestedDon()
    {
        var state = TestScene.New("OP17-058")
            .MyHandAdd("OP17-011")
            .MyHandAdd("OP17-014")
            .Build();
        var source = Card("OP17-068");
        state.Players[0].Characters.Add(source);
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });
        var handIds = state.Players[0].Hand.Select(card => card.Id.ToString()).ToArray();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(handIds));

        Assert.Empty(state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Trash.Count);
        Assert.Empty(state.Players[0].DonDeck);
        Assert.Equal(2, state.Players[0].RestDonCount);
    }

    [Fact]
    public async Task OP17_069_OnPlay_ReturnsDonAndReducesOpponentPower()
    {
        var state = TestScene.New("OP17-058").MyActiveDon(1).OppCharacter("OP17-011").Build();
        var source = Card("OP17-069");
        var target = state.Players[1].Characters[0];
        state.Players[0].Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(state.Players[0].CostArea[0].Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Equal(-2000, target.PowerModThisTurn);
        Assert.True(ActionValidator.HasKeyword(state, source, "登场回合可攻击角色"));
    }

    [Fact]
    public async Task OP17_073_OnPlay_DiscardsOneAndAddsOneActiveDon()
    {
        var state = TestScene.New("OP17-058").MyHandAdd("OP17-011").Build();
        var source = Card("OP17-073");
        state.Players[0].Characters.Add(source);
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });
        var discarded = state.Players[0].Hand[0];

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(discarded.Id.ToString()));

        Assert.Empty(state.Players[0].Hand);
        Assert.Contains(discarded, state.Players[0].Trash);
        Assert.Empty(state.Players[0].DonDeck);
        Assert.Equal(1, state.Players[0].ActiveDonCount);
    }

    [Fact]
    public async Task OP17_075_OnPlay_ReturnsTwoDonAndDiscardsOpponentHand()
    {
        var state = TestScene.New("OP17-058").MyActiveDon(2).Build();
        var source = Card("OP17-075");
        var opponentHand = Card("OP17-011");
        state.Players[0].Characters.Add(source);
        state.Players[1].Hand.Add(opponentHand);
        var donIds = state.Players[0].CostArea.Select(don => don.Id.ToString()).ToArray();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(donIds));

        Assert.Empty(state.Players[0].CostArea);
        Assert.Equal(2, state.Players[0].DonDeck.Count);
        Assert.Empty(state.Players[1].Hand);
        Assert.Contains(opponentHand, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP08_074_EndTurnDonReturn_RemainsMandatory()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var maria = Card("OP08-074");
        maria.OncePerTurnUsedKeys.Add("OP08-074-PendingReturn");
        state.Players[0].Characters.Add(maria);
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, maria, EffectTrigger.OnMyTurnEnd, prompts);

        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP08_074_RestedDonReturnPrompt_StillRequiresExactCount()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var don = state.Players[0].CostArea[0];
        don.State = DonState.Rest;
        var maria = Card("OP08-074");
        maria.OncePerTurnUsedKeys.Add("OP08-074-PendingReturn");
        state.Players[0].Characters.Add(maria);
        var prompts = new MockPromptService().QueueChoose(don.Id.ToString());

        await EffectRuntime.Resolve(state, 0, maria, EffectTrigger.OnMyTurnEnd, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompt.kind);
        Assert.Equal(1, prompt.min);
        Assert.Equal(1, prompt.max);
        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP17_077_HandlesMainCostsAndCounterDonMinus()
    {
        var main = TestScene.New("OP17-058").MyActiveDon(3).MyHandAdd("OP17-011").MyHandAdd("OP17-014").Build();
        for (int i = 0; i < 3; i++) main.Players[0].DonDeck.Add(new DonCard());

        await EffectRuntime.Resolve(main, 0, Card("OP17-077"), EffectTrigger.EventMain, new MockPromptService());
        Assert.Empty(main.Players[0].Hand);
        Assert.Equal(2, main.Players[0].Trash.Count);
        Assert.Equal(6, main.Players[0].RestDonCount);
        Assert.Empty(main.Players[0].DonDeck);

        var counter = TestScene.New().MyActiveDon(1).Build();
        await EffectRuntime.Resolve(counter, 0, Card("OP17-077"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.Empty(counter.Players[0].CostArea);
        Assert.Single(counter.Players[0].DonDeck);
        Assert.Equal(4000, counter.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP17_096_HandlesCounterConditionAndLifeTriggerRecovery()
    {
        var counter = TestScene.New("OP17-079").Build();
        var rod = Card("OP17-094");
        counter.Players[0].Characters.Add(rod);
        await EffectRuntime.Resolve(counter, 0, rod, EffectTrigger.OnEnterField, new MockPromptService());

        await EffectRuntime.Resolve(counter, 0, Card("OP17-096"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.Equal(4000, counter.Players[0].Leader.PowerModThisBattle);

        var trigger = TestScene.New().Build();
        var elbaf = Card("OP17-094");
        trigger.Players[0].Trash.Add(elbaf);
        await EffectRuntime.Resolve(trigger, 0, Card("OP17-096"), EffectTrigger.OnLifeRevealTrigger, new MockPromptService());
        Assert.DoesNotContain(elbaf, trigger.Players[0].Trash);
        Assert.Contains(elbaf, trigger.Players[0].Hand);
    }

    [Fact]
    public async Task OP17_097_HandlesMainCostReductionAndCounterPower()
    {
        var main = TestScene.New().OppCharacter("OP17-011").OppCharacter("OP17-023").Build();

        await EffectRuntime.Resolve(main, 0, Card("OP17-097"), EffectTrigger.EventMain, new MockPromptService());

        Assert.All(main.Players[1].Characters, character => Assert.Equal(-1, character.CostModThisTurn));

        var counter = TestScene.New().Build();
        await EffectRuntime.Resolve(counter, 0, Card("OP17-097"), EffectTrigger.EventCounter, new MockPromptService());
        Assert.Equal(3000, counter.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP17_085_MixedHandAndTrashPrompt_ContainsZoneMetadata()
    {
        _ = TestScene.New().Build();
        var engine = new GameEngine("op17-085-zone", ("s0", "alice", LegalOp17Deck("OP17-079")),
            ("s1", "bob", LegalOp17Deck("OP17-079")), 0, 17);
        var me = engine.State.Players[0];
        me.Hand.Clear();
        me.Trash.Clear();
        var dorry = Card("OP17-085");
        var brogyInHand = Card("OP17-092");
        var brogyInTrash = Card("OP17-092");
        me.Characters.Add(dorry);
        me.Hand.Add(brogyInHand);
        me.Trash.Add(brogyInTrash);

        var resolveTask = EffectRuntime.Resolve(engine.State, 0, dorry, EffectTrigger.OnEnterField, engine.Prompts);
        for (int i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal("OwnHandOrTrashCharacter", prompt.Kind);
        using var zones = JsonDocument.Parse(JsonSerializer.Serialize(prompt.Extra["choiceCardZones"]));
        var zoneById = zones.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("id").GetString()!,
            item => item.GetProperty("zone").GetString()!);
        Assert.Equal("hand", zoneById[brogyInHand.Id.ToString()]);
        Assert.Equal("trash", zoneById[brogyInTrash.Id.ToString()]);

        engine.Prompts.Resolve(prompt.PromptId, new[] { brogyInHand.Id.ToString() });
        await resolveTask;
    }
}
