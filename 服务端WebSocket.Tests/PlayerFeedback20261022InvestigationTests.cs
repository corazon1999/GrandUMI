using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>以现场真实开关和结算链核对旧反馈，不把正常回归记成代码修复。</summary>
public sealed class PlayerFeedback20261022InvestigationTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData(0, false, 2)]
    [InlineData(1, false, 2)]
    [InlineData(0, true, 14)]
    [InlineData(1, true, 14)]
    public async Task 葛尔兹在普通萨波下为二费而单人特征通配下为十四费(int owner, bool wildcard, int expected)
    {
        _ = TestScene.New().Build();
        const string deck = "OP13-004\nOP15-003\nOP15-004\nOP15-005\nOP15-006\nOP15-007\nOP15-008\nOP15-009\nOP15-010\nOP15-011\nOP15-012\nOP15-013";
        var engine = new GameEngine("feedback-gerd-wildcard", ("s0", "p0", deck), ("s1", "p1", deck),
            firstPlayer: 0, rngSeed: 17081, leaderKeywordWildcard: wildcard);
        var state = engine.State;
        var me = state.Players[owner];
        var gerd = Card("OP17-081");
        me.Hand.Add(gerd);
        await AtomicOps.PlayFromHandFree(state, owner, gerd);
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService().QueueConfirm(false));
        Assert.Equal(wildcard, me.Leader.Info.HasKeyword("埃鲁巴夫"));
        Assert.Equal(expected, state.CurrentCostOf(owner, gerd));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task 女帝真实KO链区分普通减至零与原本力量变零(bool originalZero, bool triggered)
    {
        var state = TestScene.New("OP14-041").AttachDonToMyLeader(1).MyCharacter("OP14-112").Build();
        var me = state.Players[0];
        var target = me.Characters[0];
        if (originalZero) target.OriginalPowerOverride = 0;
        else target.PowerModThisTurn = -target.Info.Power;
        Assert.Equal(0, state.CurrentPowerOf(0, target));
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(true);
        Assert.True(await BattleEngine.KOCardAsync(state, 0, target, prompts));
        Assert.Equal(triggered, state.Players[1].Hand.Contains(life));
        Assert.Equal(triggered ? 1 : 0, prompts.ConfirmHistory.Count);
    }
}
