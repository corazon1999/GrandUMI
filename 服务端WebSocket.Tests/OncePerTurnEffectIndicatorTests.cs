using System.Text.Json;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class OncePerTurnEffectIndicatorTests
{
    [Fact]
    public async Task ScriptedEffect_IndicatorDisappearsOnlyAfterSuccessfulActivation()
    {
        var state = TestScene.New("OP12-020").AttachDonToMyLeader(3).Build();
        var leader = state.Players[0].Leader;

        Assert.True(LeaderAvailable(state));

        // 尚未与角色战斗，发动条件不成立，不应消耗标识。
        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.True(LeaderAvailable(state));

        leader.BattledOpponentCharacterThisTurn = true;
        leader.IsTapped = true;
        await EffectRuntime.Resolve(state, 0, leader, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.False(LeaderAvailable(state));
        Assert.Contains(leader.Id, state.Players[0].OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public void OwnTurnStart_RestoresIndicatorForSurvivingCard()
    {
        var state = TestScene.New("OP12-020").Build();
        var player = state.Players[0];
        player.TurnOnceUsed.Add("OP12-020-act:" + player.Leader.Id);
        player.OncePerTurnEffectUsedCardIds.Add(player.Leader.Id);

        Assert.False(LeaderAvailable(state));

        TurnEngine.EnterResetPhase(state);

        Assert.True(LeaderAvailable(state));
        Assert.Empty(player.TurnOnceUsed);
        Assert.Empty(player.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public void DslCard_AndOrdinaryCard_AreClassifiedCorrectly()
    {
        _ = TestScene.New().Build(); // 初始化 DSL 定义目录

        Assert.True(OncePerTurnEffectCatalog.Contains("OP12-044"));
        Assert.False(OncePerTurnEffectCatalog.Contains("OP12-043"));
    }

    private static bool LeaderAvailable(GameState state)
    {
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        return snapshot.GetProperty("my").GetProperty("leaderOncePerTurnEffectAvailable").GetBoolean();
    }
}
