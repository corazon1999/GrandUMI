using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public sealed class P136P157PromoEffectTests
{
    private static readonly string[] NewNumbers =
    [
        "P-136", "P-137", "P-138", "P-139", "P-140", "P-141", "P-142", "P-143", "P-144",
        "P-145", "P-146", "P-147", "P-148", "P-149", "P-150", "P-151", "P-157",
    ];

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance CustomCharacter(string number, int power, params string[] keywords)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "红",
                Kind = CardKind.Character,
                Property = "打",
                Power = power,
                Cost = 3,
                Keywords = keywords,
            },
        };

    [Fact]
    public void AllSeventeenCardsHaveCanonicalDataAndRegisteredScripts()
    {
        _ = TestScene.New().Build();
        foreach (string number in NewNumbers)
        {
            var info = CardDatabase.Get(number);
            Assert.NotNull(info);
            Assert.NotNull(ScriptedEffectRegistry.TryGet(number));
        }

        Assert.Equal(CardKind.Stage, CardDatabase.Get("P-142")!.Kind);
        Assert.Equal("前进·梅利号", CardDatabase.Get("P-142")!.Name);
        Assert.Contains("双重攻击", CardDatabase.Get("P-137")!.Abilities);
        Assert.Contains("阻挡者", CardDatabase.Get("P-140")!.Abilities);
        Assert.Contains("速攻", CardDatabase.Get("P-141")!.Abilities);
        Assert.Contains("阻挡者", CardDatabase.Get("P-148")!.Abilities);
        Assert.Contains("速攻：角色", CardDatabase.Get("P-149")!.Abilities);
        Assert.True(OncePerTurnEffectCatalog.Contains("P-148"));
    }

    [Fact]
    public async Task RedStrawHatDonEffectsAttachOnlyRestDonAndNamiDrawsWithDon()
    {
        var state = TestScene.New("OP01-001").Build();
        var me = state.Players[0];
        var usopp = Card("P-136");
        var sanji = Card("P-137");
        var nami = Card("P-139");
        var luffy = Card("P-140");
        me.Characters.AddRange([usopp, sanji, nami, luffy]);
        var restDons = Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Rest }).ToList();
        me.CostArea.AddRange(restDons);

        await EffectRuntime.Resolve(state, 0, usopp, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(me.Leader.Id.ToString()));
        Assert.True(usopp.IsTapped);
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));

        await EffectRuntime.Resolve(state, 0, sanji, EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueChoose(sanji.Id.ToString()));
        Assert.Equal(1, me.AttachedDonCount(sanji.Id));

        await EffectRuntime.Resolve(state, 0, nami, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(nami.Id.ToString()));
        Assert.Equal(1, me.AttachedDonCount(nami.Id));
        var draw = Card("P-141");
        me.Deck.Add(draw);
        await EffectRuntime.Resolve(state, 0, nami, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.Contains(draw, me.Hand);

        await EffectRuntime.Resolve(state, 0, luffy, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(luffy.Id.ToString()).QueueOption(2));
        Assert.Equal(2, me.AttachedDonCount(luffy.Id));
        Assert.DoesNotContain(me.CostArea, don => don.State == DonState.Active);
    }

    [Fact]
    public async Task P138PowerBonusIsDynamicAndOnlyDuringOpponentTurn()
    {
        var state = TestScene.New().Build();
        var chopper = Card("P-138");
        state.Players[0].Characters.Add(chopper);
        await EffectRuntime.Resolve(state, 0, chopper, EffectTrigger.OnEnterField, new MockPromptService());

        state.CurrentTurnPlayer = 0;
        Assert.Equal(4000, state.CurrentPowerOf(0, chopper));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(6000, state.CurrentPowerOf(0, chopper));
    }

    [Fact]
    public async Task P141ReducesChosenOpponentLeaderPowerForTurn()
    {
        var state = TestScene.New().Build();
        var zoro = Card("P-141");
        state.Players[0].Characters.Add(zoro);
        var opponentLeader = state.Players[1].Leader;

        await EffectRuntime.Resolve(state, 0, zoro, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(opponentLeader.Id.ToString()));

        Assert.Equal(-1000, opponentLeader.PowerModThisTurn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P142ReplacesBattleAndEffectKo(bool byEffect)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var merry = Card("P-142");
        var victim = Card("P-140");
        me.StageCard = merry;
        me.Characters.Add(victim);
        var prompts = new MockPromptService().QueueConfirm(true);

        bool wasKOd = byEffect
            ? await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, actingSide: 1)
            : await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.False(wasKOd);
        Assert.Contains(victim, me.Characters);
        Assert.Null(me.StageCard);
        Assert.Contains(merry, me.Trash);
        Assert.Empty(state.PreventKOCardIds);
        Assert.Empty(state.PreventLeaveCardIds);
    }

    [Fact]
    public async Task P142DeclineLeavesStageAndAllowsOriginalKo()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var merry = Card("P-142");
        var victim = Card("P-140");
        me.StageCard = merry;
        me.Characters.Add(victim);

        bool wasKOd = await BattleEngine.KOCardAsync(
            state, 0, victim, new MockPromptService().QueueConfirm(false));

        Assert.True(wasKOd);
        Assert.Same(merry, me.StageCard);
        Assert.Contains(victim, me.Trash);
    }

    [Fact]
    public async Task P142DeclineThenAnotherReplacementCanResolveInChosenOrder()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var victim = Card("P-140");
        victim.IsTapped = true;
        var rosinante = Card("OP05-030");
        var merry = Card("P-142");
        me.Characters.AddRange([victim, rosinante]);
        me.StageCard = merry;
        string merryOrderToken = $"{merry.Id}:{EffectTrigger.OnAllyWillBeKOd}";
        var prompts = new MockPromptService()
            .QueueChoose(merryOrderToken)
            .QueueConfirm(false)
            .QueueConfirm(true);

        bool wasKOd = await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.False(wasKOd);
        Assert.Contains(victim, me.Characters);
        Assert.Same(merry, me.StageCard);
        Assert.Contains(rosinante, me.Trash);
        var order = Assert.Single(prompts.ChooseHistory.Where(entry => entry.kind == "EffectOrder"));
        Assert.Contains(merryOrderToken, order.choices);
        Assert.Contains($"{rosinante.Id}:{EffectTrigger.OnAllyWillBeKOd}", order.choices);
    }

    [Fact]
    public async Task P142OnePaymentProtectsAllMatchingVictimsInSimultaneousKo()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var merry = Card("P-142");
        var first = Card("P-140");
        var second = Card("P-141");
        me.StageCard = merry;
        me.Characters.AddRange([first, second]);

        int count = await BattleEngine.KOCardsSimultaneouslyAsync(
            state, 0, [first, second], new MockPromptService().QueueConfirm(true));

        Assert.Equal(0, count);
        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.Contains(merry, me.Trash);
        Assert.Empty(state.PreventKOCardIds);
        Assert.Empty(state.PreventLeaveCardIds);
    }

    [Fact]
    public async Task P142RevalidatesSourceAfterPromptAndNeverGrantsFreeProtection()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var merry = Card("P-142");
        var victim = Card("P-140");
        me.StageCard = merry;
        me.Characters.Add(victim);
        var prompts = new MutatingConfirmPromptService(() =>
        {
            me.StageCard = null;
            me.Trash.Add(merry);
        });

        bool wasKOd = await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.True(wasKOd);
        Assert.Contains(victim, me.Trash);
        Assert.Equal(1, me.Trash.Count(card => card.Id == merry.Id));
        Assert.Empty(state.PreventKOCardIds);
    }

    [Fact]
    public async Task P142PendingPromptIsPresentInReconnectAndPrivateSnapshots()
    {
        var engine = CreateEngine();
        var me = engine.State.Players[0];
        me.Characters.Clear();
        me.Trash.Clear();
        var merry = Card("P-142");
        var victim = Card("P-140");
        me.StageCard = merry;
        me.Characters.Add(victim);

        var koTask = BattleEngine.KOCardAsync(engine.State, 0, victim, engine.Prompts);
        var pending = await WaitForPrompt(engine, prompt => prompt.Kind == "Option");
        var ownerSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 0));
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(engine.State));

        Assert.Equal(pending.PromptId,
            ownerSnapshot.GetProperty("pendingPrompt").GetProperty("promptId").GetString());
        string? ownerOperationId = ownerSnapshot.GetProperty("pendingPrompt").GetProperty("operationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ownerOperationId));
        Assert.Equal(ownerOperationId,
            privateSnapshot.GetProperty("pendingPrompt").GetProperty("operationId").GetString());
        Assert.Same(merry, me.StageCard);
        Assert.Contains(victim, me.Characters);

        engine.Prompts.Resolve(pending.PromptId, ["0"]);
        Assert.False(await koTask);
        Assert.Null(engine.State.PendingPrompt);
        Assert.Contains(merry, me.Trash);
        Assert.Contains(victim, me.Characters);
    }

    [Fact]
    public async Task P143UsesCurrentCostZeroToGainRush()
    {
        var state = TestScene.New().Build();
        var crocodile = Card("P-143");
        var zeroCost = Card("P-136");
        zeroCost.CostModThisTurn = -1;
        state.Players[0].Characters.AddRange([crocodile, zeroCost]);

        await EffectRuntime.Resolve(state, 0, crocodile, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(crocodile.GainedKeywords, keyword => keyword.Keyword == "速攻");
    }

    [Fact]
    public async Task P144KoCostDrawsOnlyWhenCostWasActuallyPaid()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-144");
        var cost = Card("P-146");
        var draw = Card("P-143");
        me.Characters.AddRange([source, cost]);
        me.Deck.Add(draw);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(cost.Id.ToString()));

        Assert.Contains(cost, me.Trash);
        Assert.Contains(draw, me.Hand);
    }

    [Fact]
    public async Task P145DrawDiscardAndKoDiscardTwoResolveInPrintedOrder()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("P-145");
        var oldHand = Card("P-136");
        var draw = Card("P-137");
        me.Hand.Add(oldHand);
        me.Deck.Add(draw);
        me.Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(oldHand.Id.ToString()));
        Assert.Contains(draw, me.Hand);
        Assert.Contains(oldHand, me.Trash);

        var opponentHand = Enumerable.Range(0, 6).Select(_ => Card("P-136")).ToList();
        opponent.Hand.AddRange(opponentHand);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO,
            new MockPromptService().QueueChoose(opponentHand[1].Id.ToString(), opponentHand[4].Id.ToString()));
        Assert.Equal(4, opponent.Hand.Count);
        Assert.Contains(opponentHand[1], opponent.Trash);
        Assert.Contains(opponentHand[4], opponent.Trash);
    }

    [Fact]
    public async Task P146DrawsThenRestsCurrentZeroCostOpponentCharacter()
    {
        var state = TestScene.New().Build();
        var source = Card("P-146");
        var target = Card("P-136");
        target.CostModThisTurn = -1;
        state.Players[1].Characters.Add(target);
        var draw = Card("P-137");
        state.Players[0].Deck.Add(draw);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Contains(draw, state.Players[0].Hand);
        Assert.True(target.IsTapped);
    }

    [Fact]
    public async Task P147PowerIsDynamicAndKoCanRecoverItself()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-147");
        var extreme = Card("P-140");
        extreme.CostModThisTurn = 1;
        me.Characters.AddRange([source, extreme]);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(5000, state.CurrentPowerOf(0, source));

        extreme.CostModThisTurn = 0;
        Assert.Equal(3000, state.CurrentPowerOf(0, source));
        me.Characters.Remove(source);
        me.Trash.Add(source);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO,
            new MockPromptService().QueueChoose(source.Id.ToString()));
        Assert.Contains(source, me.Hand);
        Assert.DoesNotContain(source, me.Trash);
    }

    [Fact]
    public async Task P148ConsumesOnceOnlyAfterActivationAndAttachesOneRestDon()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-148");
        var extreme = Card("P-140");
        extreme.CostModThisTurn = 1;
        me.Characters.AddRange([source, extreme]);
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService().QueueConfirm(true).QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Contains($"P-148-act:{source.Id}", me.TurnOnceUsed);
        Assert.Contains(source.Id, me.OncePerTurnEffectUsedCardIds);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task P149ExtremeCostConditionDrawsTwoThenDiscardsOne()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-149");
        var zeroCost = Card("P-136");
        zeroCost.CostModThisTurn = -1;
        var oldHand = Card("P-137");
        var draw1 = Card("P-138");
        var draw2 = Card("P-139");
        me.Characters.AddRange([source, zeroCost]);
        me.Hand.Add(oldHand);
        me.Deck.AddRange([draw1, draw2]);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(oldHand.Id.ToString()));

        Assert.Contains(draw1, me.Hand);
        Assert.Contains(draw2, me.Hand);
        Assert.Contains(oldHand, me.Trash);
    }

    [Fact]
    public async Task P150OwnTurnRevivesTriggerCharacterAndLifeTriggerDrawsThenPreventsAttack()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-150");
        var revive = Card("OP06-108");
        me.Characters.Add(source);
        me.Trash.Add(revive);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(revive.Id.ToString()));
        Assert.Contains(revive, me.Characters);

        var draw = Card("P-136");
        me.Deck.Add(draw);
        var target = Card("P-143");
        state.Players[1].Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.Contains(draw, me.Hand);
        Assert.True(target.HasRestriction(RestrictionKind.CannotAttack));
    }

    [Fact]
    public async Task P151DiscardAddsOptionalRestDonThenSearchesTopFiveNavyCard()
    {
        var state = TestScene.New("OP02-002").Build();
        var me = state.Players[0];
        var source = Card("P-151");
        var discard = Card("P-136");
        var navy = Card("P-151");
        var other = Card("P-140");
        me.Characters.Add(source);
        me.Hand.Add(discard);
        me.Deck.AddRange([other, navy, Card("P-141"), Card("P-142"), Card("P-143")]);
        var don = new DonCard { State = DonState.InDeck };
        me.DonDeck.Add(don);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueOption(1)
            .QueueChoose(navy.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.Contains(navy, me.Hand);
        Assert.Contains(don, me.CostArea);
        Assert.Equal(DonState.Rest, don.State);
        Assert.Equal(4, me.Deck.Count);
    }

    [Fact]
    public async Task P157DiscardCanReviveLowCostElbaphCharacter()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("P-157");
        var discard = Card("P-136");
        var revive = Card("OP17-083");
        me.Characters.Add(source);
        me.Hand.Add(discard);
        me.Trash.Add(revive);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField,
            new MockPromptService()
                .QueueConfirm(true)
                .QueueChoose(discard.Id.ToString())
                .QueueChoose(revive.Id.ToString()));

        Assert.Contains(discard, me.Trash);
        Assert.Contains(revive, me.Characters);
        Assert.DoesNotContain(revive, me.Trash);
    }

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine, Func<PendingPrompt, bool> predicate, int timeoutMs = 3000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt && predicate(prompt)) return prompt;
            await Task.Delay(10);
        }
        throw new TimeoutException("等待 P-142 测试提示超时");
    }

    private static GameEngine CreateEngine()
    {
        _ = TestScene.New("OP17-039").Build();
        var leader = CardDatabase.Get("OP17-039")!;
        var pool = CardDatabase.GetBySet("OP17")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader)).ToList();
        var cards = new List<string> { leader.Number };
        var counts = new Dictionary<string, int>();
        int index = 0;
        while (cards.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            cards.Add(card.Number);
        }
        string deck = string.Join('\n', cards);
        return new GameEngine(
            "p142-prompt-snapshot", ("s0", "alice", deck), ("s1", "bob", deck),
            firstPlayer: 0, rngSeed: 142);
    }

    private sealed class MutatingConfirmPromptService(Action mutate) : IPromptService
    {
        public Task<List<string>> ChooseCards(
            int playerIdx, string kind, string text, IReadOnlyList<string> validChoices,
            int min, int max, Dictionary<string, object?>? extra = null)
            => Task.FromResult(validChoices.Take(max).ToList());

        public Task<bool> ConfirmOptional(int playerIdx, string text)
        {
            mutate();
            return Task.FromResult(true);
        }

        public Task<int> ChooseOption(
            int playerIdx, string text, IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(0);

        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
            => Task.FromResult(false);
    }
}
