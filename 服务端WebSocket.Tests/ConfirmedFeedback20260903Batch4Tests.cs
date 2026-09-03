using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第四批的规则与生命周期回归。</summary>
public sealed class ConfirmedFeedback20260903Batch4Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void 反馈诊断只从权威提示提取事件卡号与确认节点()
    {
        var state = TestScene.New().Build();
        state.PendingPrompt = new PendingPrompt
        {
            PromptId = "p50",
            PlayerIndex = 0,
            Kind = "OpponentCharacter",
            ValidChoices = [],
            Extra = new Dictionary<string, object?> { ["sourceNumber"] = "OP15-075" },
        };

        var context = Assert.IsType<FeedbackActionContext>(
            GameRoomManager.CaptureFeedbackActionContext(state, 0, "PromptResponse"));

        Assert.Equal("OP15-075", context.CardNumber);
        Assert.Equal("p50", context.PromptId);
        Assert.Equal("OpponentCharacter", context.PromptKind);
        Assert.Null(GameRoomManager.CaptureFeedbackActionContext(state, 1, "PromptResponse"));
        Assert.Null(GameRoomManager.CaptureFeedbackActionContext(state, 0, "PlayCard"));
    }

    [Fact]
    public async Task OP10_103_收取的生命牌可立即作为超新星收益目标()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var newlyTakenSupernova = Card("OP10-103");
        me.LifeArea.Add(newlyTakenSupernova);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(newlyTakenSupernova.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP10-103"), EffectTrigger.OnEnterField, prompts,
            effectExecutionId: "op10-103-new-life-target");

        Assert.Same(newlyTakenSupernova, Assert.Single(me.LifeArea));
        Assert.True(newlyTakenSupernova.IsLifeFaceUp);
        Assert.DoesNotContain(newlyTakenSupernova, me.Hand);
        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(newlyTakenSupernova.Id.ToString(), targetPrompt.choices);
    }

    [Fact]
    public async Task OP15_071_只有真实离场提交才即时清理来源持续效果()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-071")
            .MyCharacter("OP15-061")
            .Build();
        var me = state.Players[0];
        var pauly = me.Characters.Single(card => card.Info.Number == "OP15-071");
        var ohm = me.Characters.Single(card => card.Info.Number == "OP15-061");
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(
            state, 0, pauly, EffectTrigger.OnEnterField, new MockPromptService(),
            effectExecutionId: "op15-071-enter");

        Assert.Equal(6000, state.OriginalPowerOf(0, ohm));
        Assert.True(ActionValidator.HasKeyword(state, ohm, "双重攻击"));
        Assert.Equal(2, state.ContinuousEffects.Count(effect =>
            effect.SourceCardId.StartsWith(pauly.Id.ToString(), StringComparison.Ordinal)));

        // 错误持有者与重复/乱序移动没有提交离场，不能清掉仍在生效的来源。
        AtomicOps.BounceToHand(state, 1, pauly);
        Assert.Contains(pauly, me.Characters);
        Assert.Equal(6000, state.OriginalPowerOf(0, ohm));

        var leaveGuard = new ContinuousEffect
        {
            SourceCardId = me.Leader.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            LeaveGuard = "effect",
            Predicate = (_, _, card) => card.Id == pauly.Id,
        };
        state.ContinuousEffects.Add(leaveGuard);
        AtomicOps.BounceToHand(state, 0, pauly);
        Assert.Contains(pauly, me.Characters);
        Assert.Equal(6000, state.OriginalPowerOf(0, ohm));

        state.ContinuousEffects.Remove(leaveGuard);
        AtomicOps.BounceToHand(state, 0, pauly);

        Assert.DoesNotContain(pauly, me.Characters);
        Assert.Same(pauly, Assert.Single(me.Hand));
        Assert.Equal(ohm.Info.Power, state.OriginalPowerOf(0, ohm));
        Assert.False(ActionValidator.HasKeyword(state, ohm, "双重攻击"));
        Assert.DoesNotContain(state.ContinuousEffects, effect =>
            effect.SourceCardId.StartsWith(pauly.Id.ToString(), StringComparison.Ordinal));

        AtomicOps.BounceToHand(state, 0, pauly);
        Assert.Single(me.Hand);
    }

    [Fact]
    public async Task OP07_099_不同触发叠加同执行重放幂等并在己方回合末到期()
    {
        var state = TestScene.New(myLeaderNumber: "OP07-097").Build();
        var leader = state.Players[0].Leader;
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        var firstSource = Card("OP07-099");
        var secondSource = Card("OP07-099");

        await EffectRuntime.Resolve(
            state, 0, firstSource, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(leader.Id.ToString()),
            effectExecutionId: "life-trigger-1");
        var stateAfterFirst = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var hashAfterFirst = RoomRecoverySnapshotStore.ComputeStateSha256(stateAfterFirst);

        await EffectRuntime.Resolve(
            state, 0, firstSource, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(leader.Id.ToString()),
            effectExecutionId: "life-trigger-1");
        var stateAfterRetry = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(hashAfterFirst, RoomRecoverySnapshotStore.ComputeStateSha256(stateAfterRetry));

        await EffectRuntime.Resolve(
            state, 0, secondSource, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(leader.Id.ToString()),
            effectExecutionId: "life-trigger-2");

        Assert.Equal(leader.Info.Power + 4000, state.CurrentPowerOf(0, leader));
        Assert.Equal(2, state.ContinuousEffects.Count(effect =>
            string.Equals(effect.SourceCardNumber, "OP07-099", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(state.ContinuousEffects, effect => effect.SourceCardId.EndsWith(
            ":OP07-099:life-trigger-1", StringComparison.Ordinal));
        Assert.Contains(state.ContinuousEffects, effect => effect.SourceCardId.EndsWith(
            ":OP07-099:life-trigger-2", StringComparison.Ordinal));

        state.CurrentTurnPlayer = 0;
        state.TurnCount = 5;
        TurnEngine.EnterEndPhase(state);

        Assert.Equal(leader.Info.Power, state.CurrentPowerOf(0, leader));
        Assert.DoesNotContain(state.ContinuousEffects, effect =>
            string.Equals(effect.SourceCardNumber, "OP07-099", StringComparison.OrdinalIgnoreCase));
    }
}
