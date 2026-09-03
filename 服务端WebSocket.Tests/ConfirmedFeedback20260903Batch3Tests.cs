using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第三批：同时 KO、原子成本、触发无效化与恢复边界。</summary>
public sealed class ConfirmedFeedback20260903Batch3Tests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP17_098_同批次两张目标只支付一次薇薇成本并全部留场()
    {
        var state = TestScene.New(myLeaderNumber: "EB03-001").Build();
        var defender = state.Players[0];
        var attacker = state.Players[1];
        var first = Card("EB01-049");
        var second = Card("EB01-049");
        var discard = Card("OP15-003");
        var highCostEnabler = Card("OP17-085");
        highCostEnabler.CostModThisTurn = 7;
        defender.Characters.AddRange([first, second]);
        defender.Hand.Add(discard);
        attacker.Characters.Add(highCostEnabler);
        attacker.CostArea.AddRange(Enumerable.Range(0, 6)
            .Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(
            state, 1, Card("OP17-098"), EffectTrigger.EventMain, prompts);

        Assert.Equal(0, attacker.ActiveDonCount);
        Assert.Equal(6, attacker.RestDonCount);
        Assert.Contains(first, defender.Characters);
        Assert.Contains(second, defender.Characters);
        Assert.Contains(discard, defender.Trash);
        Assert.Empty(defender.Hand);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Single(prompts.ChooseHistory, prompt => prompt.kind == "OwnHand");
        Assert.Single(defender.TurnOnceUsed, key => key.StartsWith("EB03-001-guard:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EB03_001_选择等待期间手牌实例离区则整笔守护失败且不消费每回合一次()
    {
        var state = TestScene.New(myLeaderNumber: "EB03-001").Build();
        var defender = state.Players[0];
        var attacker = state.Players[1];
        var first = Card("EB01-049");
        var second = Card("EB01-049");
        var discard = Card("OP15-003");
        var highCostEnabler = Card("OP17-085");
        highCostEnabler.CostModThisTurn = 7;
        defender.Characters.AddRange([first, second]);
        defender.Hand.Add(discard);
        attacker.Characters.Add(highCostEnabler);
        attacker.CostArea.AddRange(Enumerable.Range(0, 6)
            .Select(_ => new DonCard { State = DonState.Active }));
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueChoose(discard.Id.ToString());
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "OwnHand" || !defender.Hand.Remove(discard)) return;
            defender.Deck.Add(discard);
        };

        await EffectRuntime.Resolve(
            state, 1, Card("OP17-098"), EffectTrigger.EventMain, prompts);

        Assert.DoesNotContain(first, defender.Characters);
        Assert.DoesNotContain(second, defender.Characters);
        Assert.Contains(first, defender.Trash);
        Assert.Contains(second, defender.Trash);
        Assert.Contains(discard, defender.Deck);
        Assert.DoesNotContain(defender.TurnOnceUsed,
            key => key.StartsWith("EB03-001-guard:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OP10_098_同批次两张目标只支付一次可比三卡成本并全部留场()
    {
        var state = TestScene.New(oppLeaderNumber: "OP11-001").Build();
        var defender = state.Players[1];
        var high = Card("EB01-049");
        var low = Card("OP02-094");
        var costs = new[] { Card("ST30-002"), Card("ST30-003"), Card("ST30-004") };
        defender.Characters.AddRange([high, low]);
        defender.Trash.AddRange(costs);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(high.Id.ToString())
            .QueueChoose(low.Id.ToString())
            .QueueChoose(costs[2].Id.ToString(), costs[0].Id.ToString(), costs[1].Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP10-098"), EffectTrigger.EventMain, prompts);

        Assert.Contains(high, defender.Characters);
        Assert.Contains(low, defender.Characters);
        Assert.Empty(defender.Trash);
        Assert.Equal([costs[2], costs[0], costs[1]], defender.Deck);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Single(defender.TurnOnceUsed, key => key.StartsWith("OP11-001-guard:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OP11_001_三卡成本任一实例在提交前离区则不得部分放回或授予守护()
    {
        var state = TestScene.New(oppLeaderNumber: "OP11-001").Build();
        var defender = state.Players[1];
        var high = Card("EB01-049");
        var low = Card("OP02-094");
        var costs = new[] { Card("ST30-002"), Card("ST30-003"), Card("ST30-004") };
        defender.Characters.AddRange([high, low]);
        defender.Trash.AddRange(costs);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(false)
            .QueueConfirm(false)
            .QueueChoose(high.Id.ToString())
            .QueueChoose(low.Id.ToString())
            .QueueChoose(costs.Select(card => card.Id.ToString()).ToArray());
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "OwnTrashToDeckBottom" || !defender.Trash.Remove(costs[2])) return;
            defender.Hand.Add(costs[2]);
        };

        await EffectRuntime.Resolve(
            state, 0, Card("OP10-098"), EffectTrigger.EventMain, prompts);

        Assert.Empty(defender.Characters);
        Assert.Empty(defender.Deck);
        Assert.Contains(costs[0], defender.Trash);
        Assert.Contains(costs[1], defender.Trash);
        Assert.Contains(costs[2], defender.Hand);
        Assert.Contains(high, defender.Trash);
        Assert.Contains(low, defender.Trash);
        Assert.DoesNotContain(defender.TurnOnceUsed,
            key => key.StartsWith("OP11-001-guard:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OP05_119_登场成本可精确选择活跃休息和附着咚并完成全部收益()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP05-119");
        var other = Card("OP15-003");
        me.Characters.AddRange([source, other]);
        var dons = Enumerable.Range(0, 10)
            .Select(index => new DonCard
            {
                State = index < 4 ? DonState.Active : index < 7 ? DonState.Rest : DonState.Attached,
                AttachedToCardId = index < 7 ? null : index == 7 ? me.Leader.Id : other.Id,
            })
            .ToArray();
        me.CostArea.AddRange(dons);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(dons.Select(don => don.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(me.CostArea);
        Assert.Equal(dons, me.DonDeck);
        Assert.All(dons, don =>
        {
            Assert.Equal(DonState.InDeck, don.State);
            Assert.Null(don.AttachedToCardId);
        });
        Assert.Contains(source, me.Characters);
        Assert.DoesNotContain(other, me.Characters);
        Assert.Contains(other, me.Deck);
        Assert.True(state.ExtraTurnPending);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompt.kind);
        Assert.Equal(0, prompt.min);
        Assert.Equal(10, prompt.max);
        Assert.Equal(10, prompt.choices.Count);
    }

    [Fact]
    public async Task OP05_119_咚选择等待期间一张实例离区则其余九张保持原状且无收益()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP05-119");
        var other = Card("OP15-003");
        me.Characters.AddRange([source, other]);
        var dons = Enumerable.Range(0, 10)
            .Select(index => new DonCard
            {
                State = index < 3 ? DonState.Active : index < 6 ? DonState.Rest : DonState.Attached,
                AttachedToCardId = index < 6 ? null : other.Id,
            })
            .ToArray();
        var originalStates = dons.ToDictionary(don => don.Id, don => (don.State, don.AttachedToCardId));
        me.CostArea.AddRange(dons);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(dons.Select(don => don.Id.ToString()).ToArray());
        prompts.OnChooseResponse = kind =>
        {
            if (kind != "ReturnOwnDon" || !me.CostArea.Remove(dons[9])) return;
            dons[9].State = DonState.InDeck;
            dons[9].AttachedToCardId = null;
            me.DonDeck.Add(dons[9]);
        };

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(9, me.CostArea.Count);
        Assert.Same(dons[9], Assert.Single(me.DonDeck));
        Assert.All(dons.Take(9), don =>
        {
            Assert.Contains(don, me.CostArea);
            Assert.Equal(originalStates[don.Id].State, don.State);
            Assert.Equal(originalStates[don.Id].AttachedToCardId, don.AttachedToCardId);
        });
        Assert.Contains(other, me.Characters);
        Assert.DoesNotContain(other, me.Deck);
        Assert.False(state.ExtraTurnPending);
    }

    [Fact]
    public async Task OP05_119_重复咚实例ID不满足固定十张成本()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP05-119");
        var other = Card("OP15-003");
        me.Characters.AddRange([source, other]);
        var dons = Enumerable.Range(0, 10)
            .Select(_ => new DonCard { State = DonState.Rest })
            .ToArray();
        me.CostArea.AddRange(dons);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(Enumerable.Repeat(dons[0].Id.ToString(), 10).ToArray());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(dons, me.CostArea);
        Assert.Empty(me.DonDeck);
        Assert.Contains(other, me.Characters);
        Assert.False(state.ExtraTurnPending);
    }

    [Fact]
    public async Task OP05_119_启动主要先休息一张活跃咚再追加最多一张且同回合不可重复()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP05-119");
        me.Characters.Add(source);
        var active = new DonCard { State = DonState.Active };
        var rested = new DonCard { State = DonState.Rest };
        var added = new DonCard { State = DonState.InDeck };
        me.CostArea.AddRange([active, rested]);
        me.DonDeck.Add(added);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Equal(DonState.Rest, active.State);
        Assert.Equal(DonState.Rest, rested.State);
        Assert.Equal(DonState.Active, added.State);
        Assert.Contains(added, me.CostArea);
        Assert.Empty(me.DonDeck);
        Assert.Contains($"OP05-119-act:{source.Id}", me.TurnOnceUsed);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Equal(3, me.CostArea.Count);
        Assert.Equal(1, me.ActiveDonCount);
        Assert.Equal(2, me.RestDonCount);
    }

    [Fact]
    public async Task OP05_119_没有活跃咚时不消费每回合一次也不从咚卡组追加()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP05-119");
        var rested = new DonCard { State = DonState.Rest };
        var deckDon = new DonCard { State = DonState.InDeck };
        me.Characters.Add(source);
        me.CostArea.Add(rested);
        me.DonDeck.Add(deckDon);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Same(rested, Assert.Single(me.CostArea));
        Assert.Same(deckDon, Assert.Single(me.DonDeck));
        Assert.DoesNotContain($"OP05-119-act:{source.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task OP07_085_只能牺牲角色且将OP16_096置入废弃区不会触发KO时()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP07-085");
        var yamato = Card("OP16-096");
        var invalidLegacyEvent = Card("OP07-096");
        var reviveCandidate = Card("EB01-007");
        me.Characters.AddRange([source, yamato, invalidLegacyEvent]);
        me.Trash.Add(reviveCandidate);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(yamato.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(CardKind.Event, invalidLegacyEvent.Info.Kind);
        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnCharacter", costPrompt.kind);
        Assert.Contains(source.Id.ToString(), costPrompt.choices);
        Assert.Contains(yamato.Id.ToString(), costPrompt.choices);
        Assert.DoesNotContain(invalidLegacyEvent.Id.ToString(), costPrompt.choices);
        Assert.Contains(source, me.Characters);
        Assert.Contains(invalidLegacyEvent, me.Characters);
        Assert.DoesNotContain(yamato, me.Characters);
        Assert.Contains(yamato, me.Trash);
        Assert.Contains(reviveCandidate, me.Trash);
        Assert.DoesNotContain(reviveCandidate, me.Characters);
    }

    [Fact]
    public async Task OP09_081_无效洛基登场时但仍注册洛基静态费用并联动萨波力量()
    {
        var state = TestScene.New("OP09-081", "OP13-004").Build();
        var teach = state.Players[0];
        var sabo = state.Players[1];
        var discard = Card("OP15-003");
        var target = Card("OP02-094");
        var loki = Card("OP17-119");
        teach.Hand.Add(discard);
        teach.Characters.Add(target);
        sabo.Characters.Add(loki);
        sabo.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = sabo.Leader.Id,
        });
        await EffectRuntime.Resolve(
            state, 0, teach.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 1, sabo.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, teach.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));
        var lokiPrompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 1, loki, EffectTrigger.OnEnterField, lokiPrompts);

        Assert.Contains(target, teach.Characters);
        Assert.DoesNotContain(target, teach.Trash);
        Assert.Equal(18, state.CurrentCostOf(1, loki));
        Assert.Equal(6000, state.CurrentPowerOf(1, sabo.Leader));
        Assert.Equal(12000, state.CurrentPowerOf(1, loki));
        Assert.Empty(lokiPrompts.ChooseHistory);
        Assert.Single(state.NullifiedEffectExecutionKeys);
    }

    [Fact]
    public async Task 选择性登场无效按执行ID幂等且新执行可在无效来源消失后正常结算()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var discard = Card("OP15-003");
        var target = Card("OP02-094");
        var loki = Card("OP17-119");
        me.Hand.Add(discard);
        me.Characters.Add(target);
        opponent.Characters.Add(loki);
        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true).QueueChoose(discard.Id.ToString()));

        await EffectRuntime.Resolve(
            state, 1, loki, EffectTrigger.OnEnterField, new MockPromptService(),
            effectExecutionId: "restore-fx-1");
        state.ContinuousEffects.RemoveAll(effect => effect.NullifyOnlyTrigger == EffectTrigger.OnEnterField);

        await EffectRuntime.Resolve(
            state, 1, loki, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()),
            effectExecutionId: "restore-fx-1");

        Assert.Contains(target, me.Characters);
        Assert.Single(state.NullifiedEffectExecutionKeys);

        await EffectRuntime.Resolve(
            state, 1, loki, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()),
            effectExecutionId: "restore-fx-2");

        Assert.DoesNotContain(target, me.Characters);
        Assert.Contains(target, me.Trash);
        Assert.Single(state.NullifiedEffectExecutionKeys);
        Assert.Contains(state.NullifiedEffectExecutionKeys,
            key => key.StartsWith("restore-fx-1|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OP09_081_只无效登场时且不会吞掉同卡的KO时效果()
    {
        var state = TestScene.New("OP09-081").Build();
        var me = state.Players[0];
        var source = Card("OP16-096");
        var candidate = Card("EB01-007");
        me.Trash.AddRange([source, candidate]);
        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnKO,
            new MockPromptService().QueueChoose(candidate.Id.ToString()));

        Assert.Contains(candidate, me.Characters);
        Assert.DoesNotContain(candidate, me.Trash);
        Assert.Empty(state.NullifiedEffectExecutionKeys);
    }

    [Fact]
    public void 执行去重状态只进入私有恢复快照且提示操作ID保持稳定()
    {
        var state = TestScene.New().Build();
        state.EffectExecutionSequence = 17;
        state.NullifiedEffectExecutionKeys.Add("restore-fx-1|card|OnEnterField");
        state.PendingPrompt = new PendingPrompt
        {
            PromptId = "prompt-stable-1",
            PlayerIndex = 0,
            Kind = "ChooseCards",
            ValidChoices = ["choice-a"],
            MinChoose = 1,
            MaxChoose = 1,
            PromptText = "选择一张卡",
            ResumeKey = "resume",
        };

        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(17, privateSnapshot.GetProperty("effectExecutionSequence").GetInt64());
        Assert.Equal("restore-fx-1|card|OnEnterField",
            privateSnapshot.GetProperty("nullifiedEffectExecutionKeys")[0].GetString());
        Assert.Equal("prompt-stable-1",
            privateSnapshot.GetProperty("pendingPrompt").GetProperty("operationId").GetString());

        var fullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.Equal(17, fullCheckpoint.GetProperty("EffectExecutionSequence").GetInt64());
        Assert.Equal("restore-fx-1|card|OnEnterField",
            fullCheckpoint.GetProperty("nullifiedEffectExecutionKeys")[0].GetString());

        var playerSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal("prompt-stable-1",
            playerSnapshot.GetProperty("pendingPrompt").GetProperty("operationId").GetString());
        Assert.False(playerSnapshot.GetRawText().Contains(
            "nullifiedEffectExecutionKeys", StringComparison.OrdinalIgnoreCase));
        Assert.False(playerSnapshot.GetRawText().Contains(
            "effectExecutionSequence", StringComparison.OrdinalIgnoreCase));

        var publicCheckpoint = DeterministicReplayCheckpointProvider.BuildPublicState(state).GetRawText();
        Assert.False(publicCheckpoint.Contains(
            "nullifiedEffectExecutionKeys", StringComparison.OrdinalIgnoreCase));
        Assert.False(publicCheckpoint.Contains(
            "effectExecutionSequence", StringComparison.OrdinalIgnoreCase));
    }
}
