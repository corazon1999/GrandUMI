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

        TurnEngine.EnterEndPhase(state);

        // 规则次数已为新回合恢复，但显示标识保持到控制者自己的回合开始。
        Assert.Empty(player.TurnOnceUsed);
        Assert.False(LeaderAvailable(state));

        TurnEngine.EnterResetPhase(state);

        Assert.True(LeaderAvailable(state));
        Assert.Empty(player.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public void DslCard_AndOrdinaryCard_AreClassifiedCorrectly()
    {
        _ = TestScene.New().Build(); // 初始化 DSL 定义目录

        Assert.True(OncePerTurnEffectCatalog.Contains("OP12-044"));
        Assert.False(OncePerTurnEffectCatalog.Contains("OP12-043"));
    }

    [Theory]
    [InlineData("OP01-052")]
    [InlineData("OP09-027")]
    [InlineData("ST11-001")]
    [InlineData("ST18-003")]
    [InlineData("OP10-003")]
    [InlineData("ST34-001")]
    [InlineData("OP17-001")]
    [InlineData("OP17-010")]
    [InlineData("OP17-020")]
    [InlineData("OP17-025")]
    [InlineData("OP17-030")]
    [InlineData("OP17-034")]
    [InlineData("OP17-040")]
    [InlineData("OP17-048")]
    [InlineData("OP17-049")]
    [InlineData("OP17-053")]
    [InlineData("OP17-058")]
    [InlineData("OP17-062")]
    [InlineData("OP17-063")]
    [InlineData("OP17-064")]
    [InlineData("OP17-072")]
    [InlineData("OP17-101")]
    public void 全量审计发现的每回合一次卡牌_均会下发可用标识(string cardNumber)
    {
        _ = TestScene.New().Build();

        Assert.True(OncePerTurnEffectCatalog.Contains(cardNumber));
    }

    private static bool LeaderAvailable(GameState state)
    {
        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        return snapshot.GetProperty("my").GetProperty("leaderOncePerTurnEffectAvailable").GetBoolean();
    }
}
