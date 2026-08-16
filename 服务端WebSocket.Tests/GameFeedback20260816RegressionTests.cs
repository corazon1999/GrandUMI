using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>游戏内 F 反馈 2026-08-16 的定向回归。</summary>
public class GameFeedback20260816RegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("OP13-080")]
    [InlineData("OP13-083")]
    [InlineData("OP13-084")]
    [InlineData("OP13-089")]
    [InlineData("OP13-091")]
    public async Task OP13_079_CanTrashAnyFiveElderAsOwnCostDespiteEffectLeaveProtection(
        string targetNumber)
    {
        var state = TestScene.New("OP13-079").MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        for (int i = 0; i < 7; i++) me.Trash.Add(Card("OP15-003"));

        var target = Card(targetNumber);
        me.Characters.Add(target);

        // 部分五老星在废弃区达到 7 张后会注册“不会因对方效果离场”的保护。
        // 伊姆将己方角色作为发动成本时必须能够绕过这类保护。
        if (targetNumber is not "OP13-083")
            await EffectRuntime.Resolve(state, 0, target, EffectTrigger.OnEnterField, new MockPromptService());

        var drawCard = me.Deck[0];
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.DoesNotContain(target, me.Characters);
        Assert.Contains(target, me.Trash);
        Assert.Contains(drawCard, me.Hand);
    }

    [Theory]
    [InlineData("OP13-080")]
    [InlineData("OP13-083")]
    [InlineData("OP13-084")]
    [InlineData("OP13-089")]
    [InlineData("OP13-091")]
    public async Task FiveElders_WithSevenCardsInTrash_CannotLeaveByOpponentEffect(
        string targetNumber)
    {
        var state = TestScene.New("OP13-079").Build();
        var me = state.Players[0];
        for (int i = 0; i < 7; i++) me.Trash.Add(Card("OP15-003"));

        var target = Card(targetNumber);
        me.Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, target, EffectTrigger.OnEnterField, new MockPromptService());

        bool wasKOd = await AtomicOps.KOByEffectAsync(
            state, 0, target, new MockPromptService(), actingSide: 1);

        Assert.True(state.IsLeaveGuarded(target, "effect"));
        Assert.False(wasKOd);
        Assert.Contains(target, me.Characters);
        Assert.DoesNotContain(target, me.Trash);
    }

    [Theory]
    [InlineData("OP13-080")]
    [InlineData("OP13-083")]
    [InlineData("OP13-084")]
    [InlineData("OP13-089")]
    [InlineData("OP13-091")]
    public async Task FiveElders_BelowSevenCardsInTrash_CanLeaveByOpponentEffect(
        string targetNumber)
    {
        var state = TestScene.New("OP13-079").Build();
        var me = state.Players[0];
        for (int i = 0; i < 6; i++) me.Trash.Add(Card("OP15-003"));

        var target = Card(targetNumber);
        me.Characters.Add(target);
        await EffectRuntime.Resolve(state, 0, target, EffectTrigger.OnEnterField, new MockPromptService());

        bool wasKOd = await AtomicOps.KOByEffectAsync(
            state, 0, target, new MockPromptService(), actingSide: 1);

        Assert.False(state.IsLeaveGuarded(target, "effect"));
        Assert.True(wasKOd);
        Assert.DoesNotContain(target, me.Characters);
        Assert.Contains(target, me.Trash);
    }

    [Fact]
    public async Task OP17_119_CannotKO_OP13_083_WhenImuTrashHasAtLeastSevenCards()
    {
        var state = TestScene.New("OP13-079").Build();
        var imu = state.Players[0];
        for (int i = 0; i < 10; i++) imu.Trash.Add(Card("OP15-003"));

        var saturn = Card("OP13-083");
        imu.Characters.Add(saturn);
        await EffectRuntime.Resolve(state, 0, saturn, EffectTrigger.OnEnterField, new MockPromptService());

        var killer = Card("OP17-119");
        state.Players[1].Characters.Add(killer);
        var prompts = new MockPromptService().QueueChoose(saturn.Id.ToString());

        await EffectRuntime.Resolve(state, 1, killer, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(saturn, imu.Characters);
        Assert.DoesNotContain(saturn, imu.Trash);
    }
}
