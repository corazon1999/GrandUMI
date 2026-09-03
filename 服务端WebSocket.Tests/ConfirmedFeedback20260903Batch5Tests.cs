using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第五批：回放链路与咚!!锁定提示协议回归。</summary>
public sealed class ConfirmedFeedback20260903Batch5Tests
{
    [Fact]
    public async Task ST14_017_动态加费会联动宇宙对接6_撒谎布和黑路飞阻挡者()
    {
        var state = TestScene.New(myLeaderNumber: "OP17-079")
            .MyDeckTop("OP17-119")
            .Build();
        var me = state.Players[0];

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());

        var sunny = Card("ST14-017");
        me.StageCard = sunny;
        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        var usopp = Card("OP17-080");
        me.Characters.Add(usopp);
        await EffectRuntime.Resolve(
            state, 0, usopp, EffectTrigger.OnEnterField, new MockPromptService());

        var cosmicDock = Card("OP15-088");
        me.Characters.Add(cosmicDock);
        await EffectRuntime.Resolve(
            state, 0, cosmicDock, EffectTrigger.OnEnterField,
            new MockPromptService().QueueConfirm(false));

        Assert.Equal(12, state.CurrentCostOf(0, cosmicDock));
        Assert.Equal(5_000, state.CurrentPowerOf(0, usopp));

        var snapshot = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 0));
        var field = snapshot.GetProperty("my").GetProperty("fieldCards");
        var cosmicSnapshot = field.EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "OP15-088");
        var usoppSnapshot = field.EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == "OP17-080");

        Assert.Equal(12, cosmicSnapshot.GetProperty("cost").GetInt32());
        Assert.Contains(
            cosmicSnapshot.GetProperty("gainedKeywords").EnumerateArray(),
            keyword => keyword.GetString() == "阻挡者");
        Assert.Equal(5_000, usoppSnapshot.GetProperty("powerCurrent").GetInt32());
    }

    [Fact]
    public async Task ST27_005_回放中的原费5减至当前3目标可选且能KO()
    {
        var state = TestScene.New().Build();
        var source = Card("ST27-005");
        state.Players[0].Characters.Add(source);
        var target = Card("OP10-030");
        target.CostModThisTurn = -2;
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(3, state.CurrentCostOf(1, target));
        Assert.Contains(target.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task 多张被锁咚会向双方视角下发数量_并在重置消费后自动消失()
    {
        var state = TestScene.New().Build();
        var opponent = state.Players[1];
        var first = new DonCard { State = DonState.Rest };
        var second = new DonCard { State = DonState.Rest };
        var unlocked = new DonCard { State = DonState.Rest };
        opponent.CostArea.AddRange([first, second, unlocked]);

        await EffectRuntime.Resolve(
            state, 0, Card("OP07-026"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(first.Id.ToString()));
        await EffectRuntime.Resolve(
            state, 0, Card("OP07-026"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(second.Id.ToString()));

        Assert.True(first.CannotActivateNextReset);
        Assert.True(second.CannotActivateNextReset);
        Assert.False(unlocked.CannotActivateNextReset);

        var player0 = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 0));
        var player1 = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 1));
        var spectator = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: -1));
        Assert.Equal(2, player0.GetProperty("opponent").GetProperty("costNextResetInactive").GetInt32());
        Assert.Equal(2, player1.GetProperty("my").GetProperty("costNextResetInactive").GetInt32());
        Assert.Equal(2, spectator.GetProperty("opponent").GetProperty("costNextResetInactive").GetInt32());

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var costArea = privateState.GetProperty("players")[1].GetProperty("costArea");
        Assert.Equal(2, costArea.EnumerateArray()
            .Count(don => don.GetProperty("cannotActivateNextReset").GetBoolean()));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterResetPhase(state);

        Assert.Equal(DonState.Rest, first.State);
        Assert.Equal(DonState.Rest, second.State);
        Assert.Equal(DonState.Active, unlocked.State);
        Assert.All(opponent.CostArea, don => Assert.False(don.CannotActivateNextReset));
        var afterReset = JsonSerializer.SerializeToElement(
            StateSnapshotBuilder.Build(state, viewerIndex: 1));
        Assert.Equal(0, afterReset.GetProperty("my").GetProperty("costNextResetInactive").GetInt32());
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
