using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP11_106_ZeusTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OpponentEffectKO_AllowsLaboonToPreventLeave()
    {
        var state = TestScene.New().Build();
        var protectedSide = state.Players[0];
        var zeusSide = state.Players[1];

        // 复现反馈场景：领袖和目标已休息，仅拉布与另一张角色可用于支付横置两张卡牌的成本。
        protectedSide.Leader.IsTapped = true;
        var laboon = Card("OP15-035");
        var victim = Card("OP10-030");
        victim.IsTapped = true;
        var secondRestCost = Card("ST32-003");
        protectedSide.Characters.AddRange([laboon, victim, secondRestCost]);

        var zeus = Card("OP11-106");
        var lifeCost = Card("OP15-003");
        zeusSide.Characters.Add(zeus);
        zeusSide.LifeArea.Add(lifeCost);

        var prompts = new MockPromptService()
            .QueueConfirm(true) // 宙斯支付生命成本并发动登场时效果
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true) // 拉布发动离场替代效果
            .QueueChoose(laboon.Id.ToString(), secondRestCost.Id.ToString());

        await EffectRuntime.Resolve(state, 1, zeus, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(zeusSide.LifeArea);
        Assert.Contains(lifeCost, zeusSide.Hand);
        Assert.Contains(victim, protectedSide.Characters);
        Assert.DoesNotContain(victim, protectedSide.Trash);
        Assert.True(laboon.IsTapped);
        Assert.True(secondRestCost.IsTapped);
        Assert.Contains(prompts.ConfirmHistory, text => text.StartsWith("拉布："));
    }
}
