using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP10_060_EffectTests
{
    /// <summary>
    /// OP10-060 护罩护罩 手枪应按结算时的当前力量筛选目标，
    /// 因此原本力量 12000、被减至 6000 的角色也可以被放回卡组底。
    /// 【主要】与【触发】共用相同的目标口径。
    /// </summary>
    [Theory]
    [InlineData(EffectTrigger.EventMain)]
    [InlineData(EffectTrigger.OnLifeRevealTrigger)]
    public async Task UsesCurrentPowerForTargetSelection(EffectTrigger trigger)
    {
        var state = TestScene.New()
            .OppCharacter("OP13-042")
            .OppCharacter("OP13-017")
            .Build();

        var reducedTarget = state.Players[1].Characters[0];
        var unreducedTarget = state.Players[1].Characters[1];
        Assert.Equal(12000, reducedTarget.Info.Power);
        reducedTarget.PowerModThisTurn = -6000;
        Assert.Equal(6000, state.CurrentPowerOf(1, reducedTarget));
        Assert.True(state.CurrentPowerOf(1, unreducedTarget) > 6000);

        var prompts = new MockPromptService()
            .QueueChoose(reducedTarget.Id.ToString());
        var source = new CardInstance { Info = CardDatabase.Get("OP10-060")! };

        await EffectRuntime.Resolve(state, 0, source, trigger, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.kind == "OpponentCharacter"));
        Assert.Contains(reducedTarget.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(unreducedTarget.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(reducedTarget, state.Players[1].Characters);
        Assert.Same(reducedTarget, state.Players[1].Deck[^1]);
    }
}
