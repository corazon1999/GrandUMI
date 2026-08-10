using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST15EffectTests
{
    /// <summary>
    /// ST15-002 爱德华·纽哥特【启动主要】：横置自身，KO 对方最多1张力量不高于5000的角色。
    /// 卡面未写“原本的力量”，因此应按包含临时减力在内的当前力量选择目标。
    /// </summary>
    [Fact]
    public async Task ST15_002_ActivatedMain_CanKOCharacterReducedFrom12000To5000()
    {
        var state = TestScene.New()
            .OppCharacter("OP13-042")
            .Build();

        var source = new CardInstance { Info = CardDatabase.Get("ST15-002")! };
        state.Players[0].Characters.Add(source);

        var target = state.Players[1].Characters[0];
        Assert.Equal(12000, target.Info.Power);
        target.PowerModThisTurn = -7000;
        Assert.Equal(5000, state.CurrentPowerOf(1, target));

        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory.Where(
            history => history.kind == "OpponentCharacter"));
        Assert.Contains(target.Id.ToString(), targetPrompt.choices);
        Assert.True(source.IsTapped);
        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }
}
