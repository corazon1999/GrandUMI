using System.Text.Json;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP08-002 马尔高的贴咚发动门禁回归。</summary>
public sealed class OP08_002_MarcoActivationTests
{
    [Fact]
    public async Task 未贴咚时动作入口快照与直接结算都不得发动()
    {
        var state = TestScene.New("OP08-002")
            .MyDeckTop("OP15-003")
            .Build();
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.Main;
        var me = state.Players[0];
        var deckBefore = me.Deck.ToArray();
        var prompts = new MockPromptService();

        var validation = ActionValidator.CanUseEffect(state, 0, me.Leader.Id);
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.False(validation.Ok);
        Assert.Contains("至少 1 张咚", validation.Reason);
        Assert.False(snapshot.GetProperty("my").GetProperty("leaderCanActivateEffect").GetBoolean());
        Assert.Equal(deckBefore, me.Deck);
        Assert.Empty(me.Hand);
        Assert.Empty(me.TurnOnceUsed);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task 贴一张咚后允许发动并正常结算一次()
    {
        var state = TestScene.New("OP08-002")
            .MyDeckTop("OP15-003")
            .AttachDonToMyLeader(1)
            .Build();
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.Main;
        var me = state.Players[0];
        var drawn = Assert.Single(me.Deck);
        var prompts = new MockPromptService()
            .QueueChoose(drawn.Id.ToString())
            .QueueOption(0);

        var validation = ActionValidator.CanUseEffect(state, 0, me.Leader.Id);
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.True(validation.Ok);
        Assert.True(snapshot.GetProperty("my").GetProperty("leaderCanActivateEffect").GetBoolean());
        Assert.Empty(me.Hand);
        Assert.Equal(drawn, Assert.Single(me.Deck));
        Assert.Single(me.TurnOnceUsed);
    }
}
