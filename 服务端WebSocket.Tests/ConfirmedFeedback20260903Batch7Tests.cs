using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第七批：卡牌规则与恢复边界回归。</summary>
public sealed class ConfirmedFeedback20260903Batch7Tests
{
    [Fact]
    public async Task OP13_031_提奇仅无效登场时_生命阈值阻挡者仍动态生效()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var law = Card("OP13-031");
        me.Characters.Add(law);
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004")]);

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        var prompts = new MockPromptService().QueueChooseEmpty();
        const string executionId = "batch7-op13-031-enter";
        await EffectRuntime.Resolve(
            state, 0, law, EffectTrigger.OnEnterField, prompts,
            effectExecutionId: executionId);
        // 恢复重放或重复请求再次进入同一执行时，静态注册与无效化消费都必须幂等。
        await EffectRuntime.Resolve(
            state, 0, law, EffectTrigger.OnEnterField, prompts,
            effectExecutionId: executionId);

        Assert.True(state.IsTriggerNullified(law, EffectTrigger.OnEnterField));
        Assert.Empty(prompts.ChooseHistory);
        Assert.False(ActionValidator.HasKeyword(state, law, "阻挡者"));
        Assert.Single(state.NullifiedEffectExecutionKeys);
        Assert.Single(state.ContinuousEffects.Where(effect =>
            effect.SourceCardId == law.Id.ToString()
            && effect.GrantKeyword == "阻挡者"));
        Assert.False(EffectRuntime.HasEffectForTrigger(law, EffectTrigger.OnKO));

        me.LifeArea.RemoveAt(0);
        Assert.True(ActionValidator.HasKeyword(state, law, "阻挡者"));

        me.LifeArea.Add(Card("OP15-005"));
        Assert.False(ActionValidator.HasKeyword(state, law, "阻挡者"));

        me.LifeArea.RemoveAt(me.LifeArea.Count - 1);
        Assert.True(ActionValidator.HasKeyword(state, law, "阻挡者"));

        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.Contains(checkpoint.GetProperty("continuousEffects").EnumerateArray(), effect =>
            effect.GetProperty("SourceCardId").GetString() == law.Id.ToString()
            && effect.GetProperty("GrantKeyword").GetString() == "阻挡者");
        Assert.Equal(
            checkpoint.GetRawText(),
            DeterministicReplayCheckpointProvider.BuildFullState(state).GetRawText());
    }

    [Fact]
    public async Task OP13_031_离场清掉阻挡者来源_同一实例再次登场可重新建立()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var law = Card("OP13-031");
        me.Characters.Add(law);
        me.LifeArea.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(
            state, 0, law, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());
        Assert.True(ActionValidator.HasKeyword(state, law, "阻挡者"));

        AtomicOps.BounceToHand(state, 0, law);
        Assert.DoesNotContain(state.ContinuousEffects,
            effect => effect.SourceCardId == law.Id.ToString());
        Assert.False(ActionValidator.HasKeyword(state, law, "阻挡者"));

        Assert.True(me.Hand.Remove(law));
        me.Characters.Add(law);
        await EffectRuntime.Resolve(
            state, 0, law, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        Assert.True(ActionValidator.HasKeyword(state, law, "阻挡者"));
        Assert.Single(state.ContinuousEffects.Where(effect =>
            effect.SourceCardId == law.Id.ToString()));
    }

    [Fact]
    public async Task ST10_011_力量加成持续整个对方回合并在下个我方回合开始清除()
    {
        var state = TestScene.New().Build();
        var heat = Card("ST10-011");
        state.Players[0].Characters.Add(heat);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;

        await EffectRuntime.Resolve(
            state, 0, heat, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());

        Assert.Equal(heat.Info.Power + 2_000, state.CurrentPowerOf(0, heat));
        Assert.Single(heat.PowerModsUntilNextOwnTurnStart);

        // 同一服务端回合的准备阶段重入，以及同一回合重复通知，都不得提前清除或叠加。
        TurnEngine.EnterResetPhase(state);
        await EffectRuntime.Resolve(
            state, 0, heat, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());
        Assert.Equal(heat.Info.Power + 2_000, state.CurrentPowerOf(0, heat));
        Assert.Single(heat.PowerModsUntilNextOwnTurnStart);

        TurnEngine.AdvanceTurn(state);
        Assert.Equal(1, state.CurrentTurnPlayer);
        Assert.Equal(heat.Info.Power + 2_000, state.CurrentPowerOf(0, heat));

        TurnEngine.AdvanceTurnToReset(state);
        Assert.Equal(0, state.CurrentTurnPlayer);
        Assert.Equal(heat.Info.Power, state.CurrentPowerOf(0, heat));
        Assert.Empty(heat.PowerModsUntilNextOwnTurnStart);
    }

    [Fact]
    public async Task ST10_011_追加我方回合也在紧邻的我方准备阶段精确清除()
    {
        var state = TestScene.New().Build();
        var heat = Card("ST10-011");
        state.Players[0].Characters.Add(heat);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 7;

        await EffectRuntime.Resolve(
            state, 0, heat, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());
        state.ExtraTurnPending = true;

        TurnEngine.AdvanceTurnToReset(state);

        Assert.Equal(0, state.CurrentTurnPlayer);
        Assert.Equal(8, state.TurnCount);
        Assert.Equal(heat.Info.Power, state.CurrentPowerOf(0, heat));
        Assert.Empty(heat.PowerModsUntilNextOwnTurnStart);
    }

    [Fact]
    public async Task ST10_011_同名实例分别生效且离场实例不会把旧加成带回场上()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = Card("ST10-011");
        var second = Card("ST10-011");
        me.Characters.AddRange([first, second]);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 5;

        await EffectRuntime.Resolve(
            state, 0, first, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, second, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());

        Assert.Single(first.PowerModsUntilNextOwnTurnStart);
        Assert.Single(second.PowerModsUntilNextOwnTurnStart);
        Assert.Equal(first.Info.Power + 2_000, state.CurrentPowerOf(0, first));
        Assert.Equal(second.Info.Power + 2_000, state.CurrentPowerOf(0, second));

        BattleEngine.KOCard(state, 0, first);
        Assert.Empty(first.PowerModsUntilNextOwnTurnStart);
        Assert.Single(second.PowerModsUntilNextOwnTurnStart);
        Assert.Contains(first, me.Trash);

        Assert.True(me.Trash.Remove(first));
        me.Characters.Add(first);
        Assert.Equal(first.Info.Power, state.CurrentPowerOf(0, first));
        Assert.Equal(second.Info.Power + 2_000, state.CurrentPowerOf(0, second));
    }

    [Fact]
    public async Task ST10_011_期限身份进入恢复快照与重放检查点_旧状态投影不增加空字段()
    {
        var state = TestScene.New().Build();
        var heat = Card("ST10-011");
        var unaffected = Card("OP15-088");
        state.Players[0].Characters.AddRange([heat, unaffected]);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 11;

        await EffectRuntime.Resolve(
            state, 0, heat, EffectTrigger.OnDonReturnedToDeck, new MockPromptService());

        var privateState = JsonSerializer.SerializeToElement(
            PrivateStateSnapshotBuilder.Build(state));
        var privateHeat = privateState.GetProperty("players")[0]
            .GetProperty("characters").EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "ST10-011");
        var persisted = Assert.Single(
            privateHeat.GetProperty("powerModsUntilNextOwnTurnStart").EnumerateArray());
        Assert.Equal(2_000, persisted.GetProperty("Delta").GetInt32());
        Assert.Equal(0, persisted.GetProperty("OwnerSide").GetInt32());
        Assert.Equal(11, persisted.GetProperty("AppliedTurnCount").GetInt32());

        var full = DeterministicReplayCheckpointProvider.BuildFullState(state);
        var fullHeat = full.GetProperty("players")[0]
            .GetProperty("characters").EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "ST10-011");
        Assert.Single(fullHeat.GetProperty("powerModsUntilNextOwnTurnStart").EnumerateArray());

        var fullUnaffected = full.GetProperty("players")[0]
            .GetProperty("characters").EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "OP15-088");
        Assert.False(fullUnaffected.TryGetProperty(
            "powerModsUntilNextOwnTurnStart", out _));

        var publicState = DeterministicReplayCheckpointProvider.BuildPublicState(state);
        var publicHeat = publicState.GetProperty("players")[0]
            .GetProperty("characters").EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "ST10-011");
        Assert.Single(publicHeat.GetProperty("powerModsUntilNextOwnTurnStart").EnumerateArray());
        Assert.Equal(full.GetRawText(),
            DeterministicReplayCheckpointProvider.BuildFullState(state).GetRawText());
        Assert.Equal(10, RoomRecoverySnapshotStore.SchemaVersion);
    }

    [Fact]
    public async Task OP02_085_登场收益由对方从活跃休息附着咚中自行选择()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var magellan = Card("OP02-085");
        me.Characters.Add(magellan);
        var ownDon = new DonCard { State = DonState.Active };
        me.CostArea.Add(ownDon);
        var attached = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = opponent.Leader.Id,
        };
        var opponentActive = new DonCard { State = DonState.Active };
        var opponentRest = new DonCard { State = DonState.Rest };
        opponent.CostArea.AddRange([opponentActive, opponentRest, attached]);
        var prompts = new IndexedPromptService()
            .QueueConfirm(true)
            .QueueChoose(ownDon.Id.ToString())
            .QueueChoose(attached.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, magellan, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(prompts.ChooseHistory, prompt =>
            prompt.PlayerIndex == 1
            && prompt.Kind == "ReturnOwnDon"
            && prompt.Min == 1
            && prompt.Max == 1
            && prompt.Choices.ToHashSet().SetEquals([
                opponentActive.Id.ToString(), opponentRest.Id.ToString(), attached.Id.ToString()
            ]));
        Assert.DoesNotContain(attached, opponent.CostArea);
        Assert.Contains(attached, opponent.DonDeck);
        Assert.Contains(opponentActive, opponent.CostArea);
        Assert.Contains(opponentRest, opponent.CostArea);
        Assert.Equal(opponent.Leader.Info.Power, state.CurrentPowerOf(1, opponent.Leader));
    }

    [Fact]
    public async Task OP02_085_仅在对方回合KO时由对方选择两张_不足两张则全部返回()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        var magellan = Card("OP02-085");
        var active = new DonCard { State = DonState.Active };
        var rest = new DonCard { State = DonState.Rest };
        var attached = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = opponent.Leader.Id,
        };
        opponent.CostArea.AddRange([active, rest, attached]);
        state.CurrentTurnPlayer = 1;
        var prompts = new IndexedPromptService()
            .QueueChoose(rest.Id.ToString(), attached.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, magellan, EffectTrigger.OnKO, prompts);

        Assert.Contains(active, opponent.CostArea);
        Assert.DoesNotContain(rest, opponent.CostArea);
        Assert.DoesNotContain(attached, opponent.CostArea);
        Assert.Contains(rest, opponent.DonDeck);
        Assert.Contains(attached, opponent.DonDeck);
        var forced = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(1, forced.PlayerIndex);
        Assert.Equal("ReturnOwnDon", forced.Kind);
        Assert.Equal(2, forced.Min);
        Assert.Equal(2, forced.Max);

        var scarce = TestScene.New().Build();
        // 即使只有活跃咚，也必须由卡文指定的“对方”显式完成选择。
        var only = new DonCard { State = DonState.Active };
        scarce.Players[1].CostArea.Add(only);
        scarce.CurrentTurnPlayer = 1;
        var scarcePrompts = new IndexedPromptService().QueueChoose(only.Id.ToString());

        await EffectRuntime.Resolve(
            scarce, 0, Card("OP02-085"), EffectTrigger.OnKO, scarcePrompts);

        Assert.Empty(scarce.Players[1].CostArea);
        Assert.Contains(only, scarce.Players[1].DonDeck);
        Assert.Equal(1, Assert.Single(scarcePrompts.ChooseHistory).Min);

        var ownTurn = TestScene.New().Build();
        var untouched = new DonCard { State = DonState.Rest };
        ownTurn.Players[1].CostArea.Add(untouched);
        ownTurn.CurrentTurnPlayer = 0;
        var ownTurnPrompts = new IndexedPromptService();

        await EffectRuntime.Resolve(
            ownTurn, 0, Card("OP02-085"), EffectTrigger.OnKO, ownTurnPrompts);

        Assert.Contains(untouched, ownTurn.Players[1].CostArea);
        Assert.Empty(ownTurnPrompts.ChooseHistory);
    }

    [Fact]
    public async Task OP02_085_强制选择重复或等待期间状态漂移时整笔拒绝不部分提交()
    {
        var duplicateState = TestScene.New().Build();
        duplicateState.CurrentTurnPlayer = 1;
        var first = new DonCard { State = DonState.Rest };
        var second = new DonCard { State = DonState.Attached, AttachedToCardId = duplicateState.Players[1].Leader.Id };
        duplicateState.Players[1].CostArea.AddRange([first, second]);
        var duplicatePrompts = new IndexedPromptService()
            .QueueChoose(first.Id.ToString(), first.Id.ToString());

        await EffectRuntime.Resolve(
            duplicateState, 0, Card("OP02-085"), EffectTrigger.OnKO, duplicatePrompts);

        Assert.Contains(first, duplicateState.Players[1].CostArea);
        Assert.Contains(second, duplicateState.Players[1].CostArea);
        Assert.Empty(duplicateState.Players[1].DonDeck);

        var driftState = TestScene.New().Build();
        driftState.CurrentTurnPlayer = 1;
        var stable = new DonCard { State = DonState.Rest };
        var drifted = new DonCard { State = DonState.Attached, AttachedToCardId = driftState.Players[1].Leader.Id };
        driftState.Players[1].CostArea.AddRange([stable, drifted]);
        var driftPrompts = new IndexedPromptService()
            .QueueChoose(stable.Id.ToString(), drifted.Id.ToString());
        driftPrompts.OnChooseResponse = (_, kind) =>
        {
            if (kind == "ReturnOwnDon") driftState.Players[1].CostArea.Remove(drifted);
        };

        await EffectRuntime.Resolve(
            driftState, 0, Card("OP02-085"), EffectTrigger.OnKO, driftPrompts);

        Assert.Contains(stable, driftState.Players[1].CostArea);
        Assert.DoesNotContain(stable, driftState.Players[1].DonDeck);
        Assert.DoesNotContain(drifted, driftState.Players[1].DonDeck);
    }

    [Fact]
    public async Task OP02_085_真实Prompt重连只对选择方可见_空过期重复响应都不重复结算()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        me.Characters.Clear();
        me.CostArea.Clear();
        me.DonDeck.Clear();
        opponent.CostArea.Clear();
        opponent.DonDeck.Clear();

        var magellan = Card("OP02-085");
        me.Characters.Add(magellan);
        var own = new DonCard { State = DonState.Active };
        me.CostArea.Add(own);
        var opponentRest = new DonCard { State = DonState.Rest };
        var opponentAttached = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = opponent.Leader.Id,
        };
        opponent.CostArea.AddRange([opponentRest, opponentAttached]);

        var resolution = EffectRuntime.Resolve(
            state, 0, magellan, EffectTrigger.OnEnterField, engine.Prompts);
        var confirm = await WaitForPrompt(
            engine, prompt => prompt.PlayerIndex == 0 && prompt.Kind == "Option");
        Assert.True(Respond(engine, 0, confirm, "0"));

        var ownCost = await WaitForPrompt(
            engine, prompt => prompt.PlayerIndex == 0 && prompt.Kind == "ReturnOwnDon");
        Assert.True(Respond(engine, 0, ownCost, own.Id.ToString()));

        var forced = await WaitForPrompt(
            engine, prompt => prompt.PlayerIndex == 1 && prompt.Kind == "ReturnOwnDon");
        var chooserView = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 1));
        var otherView = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 0));
        Assert.Equal(forced.PromptId,
            chooserView.GetProperty("pendingPrompt").GetProperty("operationId").GetString());
        Assert.Equal(JsonValueKind.Null, otherView.GetProperty("pendingPrompt").ValueKind);

        // 强制选择没有本地自动超时；等待期间重连可继续读取同一个服务端 operationId。
        await Task.Delay(75);
        Assert.Equal(forced.PromptId, state.PendingPrompt?.PromptId);

        Assert.False(Respond(engine, 1, ownCost, own.Id.ToString()));
        Assert.Equal(forced.PromptId, state.PendingPrompt?.PromptId);
        Assert.False(Respond(engine, 1, forced));
        Assert.Equal(forced.PromptId, state.PendingPrompt?.PromptId);

        Assert.True(Respond(engine, 1, forced, opponentAttached.Id.ToString()));
        Assert.False(Respond(engine, 1, forced, opponentAttached.Id.ToString()));
        await resolution;

        Assert.Single(opponent.CostArea);
        Assert.Contains(opponentRest, opponent.CostArea);
        Assert.DoesNotContain(opponentAttached, opponent.CostArea);
        Assert.Single(opponent.DonDeck);
        Assert.Contains(opponentAttached, opponent.DonDeck);
        Assert.Null(state.PendingPrompt);
    }

    [Fact]
    public async Task OP14_094_双方没有当前费用零或八以上角色时不抽牌弃牌()
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003", "OP15-004")
            .MyCharacter("OP14-094")
            .OppCharacter("OP15-088")
            .Build();
        var source = state.Players[0].Characters.Single(card => card.Info.Number == "OP14-094");
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Deck.Count);
        Assert.Empty(state.Players[0].Trash);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Theory]
    [InlineData(false, 8, -8, true)]  // 我方原本 8，当前 0
    [InlineData(true, 7, 1, true)]    // 对方原本 7，当前 8
    [InlineData(true, 8, -1, false)]  // 对方原本 8，当前 7
    [InlineData(false, 6, 0, false)]  // 我方当前费用位于 1~7
    public async Task OP14_094_按双方角色当前费用而非原本费用判断(
        bool targetOnOpponentSide,
        int originalCost,
        int currentCostDelta,
        bool expectedToResolve)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP14-094");
        var target = CostCharacter(originalCost);
        target.CostModThisTurn = currentCostDelta;
        me.Characters.Add(source);
        state.Players[targetOnOpponentSide ? 1 : 0].Characters.Add(target);
        me.Deck.Clear();
        me.Hand.Clear();
        me.Trash.Clear();
        me.Deck.AddRange([Card("OP15-003"), Card("OP15-004")]);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, prompts);

        if (expectedToResolve)
        {
            Assert.Empty(me.Deck);
            Assert.Single(me.Hand);
            Assert.Single(me.Trash);
            var discardPrompt = Assert.Single(prompts.ChooseHistory);
            Assert.Equal("DiscardOwnChosen", discardPrompt.kind);
            Assert.Equal(1, discardPrompt.min);
            Assert.Equal(1, discardPrompt.max);
        }
        else
        {
            Assert.Equal(2, me.Deck.Count);
            Assert.Empty(me.Hand);
            Assert.Empty(me.Trash);
            Assert.Empty(prompts.ChooseHistory);
        }
    }

    [Fact]
    public async Task OP14_094_条件成立时只抽二再从含新牌的手牌中精确弃一_不重复委托旧DSL()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP14-094");
        var threshold = CostCharacter(8);
        me.Characters.AddRange([source, threshold]);
        me.Hand.Clear();
        me.Deck.Clear();
        me.Trash.Clear();
        var oldHand = Card("OP15-003");
        var firstDraw = Card("OP15-004");
        var secondDraw = Card("OP15-005");
        me.Hand.Add(oldHand);
        me.Deck.AddRange([
            firstDraw,
            secondDraw,
            Card("OP15-006"),
            Card("OP15-007"),
        ]);
        var prompts = new MockPromptService().QueueChoose(secondDraw.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(2, me.Deck.Count);
        Assert.Equal(2, me.Hand.Count);
        Assert.Contains(oldHand, me.Hand);
        Assert.Contains(firstDraw, me.Hand);
        Assert.DoesNotContain(secondDraw, me.Hand);
        Assert.Single(me.Trash);
        Assert.Contains(secondDraw, me.Trash);
        Assert.Single(prompts.ChooseHistory);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance CostCharacter(int cost)
        => new()
        {
            Info = new CardInfo
            {
                Number = $"TEST-B7-{cost:00}",
                Name = $"第七批费用测试角色{cost}",
                Color = "红",
                Kind = CardKind.Character,
                Property = "特",
                Power = 1_000,
                Cost = cost,
            },
        };

    private static GameEngine CreateEngine()
    {
        const string leaderNumber = "OP17-039";
        _ = TestScene.New(leaderNumber);
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP17")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leader.Number };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            lines.Add(card.Number);
        }

        var deck = string.Join('\n', lines);
        return new GameEngine(
            "confirmed-feedback-batch7-prompt",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260903);
    }

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine,
        Func<PendingPrompt, bool> predicate,
        int timeoutMs = 3_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt && predicate(prompt)) return prompt;
            await Task.Delay(10);
        }

        throw new TimeoutException("等待第七批回归测试 Prompt 超时");
    }

    private static bool Respond(
        GameEngine engine,
        int playerIndex,
        PendingPrompt prompt,
        params string[] chosen)
        => engine.HandleAction(
            playerIndex,
            "PromptResponse",
            JsonSerializer.SerializeToElement(new
            {
                promptId = prompt.PromptId,
                chosen,
            }));

    private sealed class IndexedPromptService : IPromptService
    {
        private readonly Queue<List<string>> _answers = new();
        private readonly Queue<bool> _confirms = new();

        public List<(int PlayerIndex, string Kind, IReadOnlyList<string> Choices, int Min, int Max)> ChooseHistory { get; } = new();
        public Action<int, string>? OnChooseResponse { get; set; }

        public IndexedPromptService QueueChoose(params string[] choices)
        {
            _answers.Enqueue(choices.ToList());
            return this;
        }

        public IndexedPromptService QueueConfirm(bool answer)
        {
            _confirms.Enqueue(answer);
            return this;
        }

        public Task<List<string>> ChooseCards(
            int playerIdx,
            string kind,
            string text,
            IReadOnlyList<string> validChoices,
            int min,
            int max,
            Dictionary<string, object?>? extra = null)
        {
            ChooseHistory.Add((playerIdx, kind, validChoices, min, max));
            var answer = _answers.Count > 0
                ? _answers.Dequeue().Where(validChoices.Contains).ToList()
                : validChoices.Take(max).ToList();
            OnChooseResponse?.Invoke(playerIdx, kind);
            return Task.FromResult(answer);
        }

        public Task<bool> ConfirmOptional(int playerIdx, string text)
            => Task.FromResult(_confirms.Count == 0 || _confirms.Dequeue());

        public Task<int> ChooseOption(
            int playerIdx,
            string text,
            IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(0);

        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
            => Task.FromResult(false);
    }
}
