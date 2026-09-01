using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-01 反馈筛选后确认修复的规则回归。</summary>
public sealed class FeedbackTriage20260901RegressionTests
{
    [Fact]
    public void OP11_022_基础禁攻应同时进入动作校验与重连快照()
    {
        var state = TestScene.New("OP11-022").Build();
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        var leader = state.Players[0].Leader;

        Assert.Contains("此角色无法攻击", leader.Info.Abilities);
        Assert.True(ActionValidator.HasCannotAttackStatus(state, leader));
        Assert.False(ActionValidator.CanAttack(state, 0, leader.Id, targetIsLeader: true, targetId: null).Ok);

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.True(snapshot.GetProperty("my").GetProperty("leaderCannotAttack").GetBoolean());
        Assert.False(snapshot.GetProperty("my").GetProperty("leaderCanAttack").GetBoolean());

        // 整卡效果被无效时，卡牌自身的不利持续效果也应一并失效。
        leader.IsEffectsNullified = true;
        Assert.False(ActionValidator.HasCannotAttackStatus(state, leader));
        Assert.True(ActionValidator.CanAttack(state, 0, leader.Id, targetIsLeader: true, targetId: null).Ok);
    }
}
