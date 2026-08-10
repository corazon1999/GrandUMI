using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP16_108_ShiryuTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 登场效果将废弃区卡牌正面朝上加入生命区最上方()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP16-108");
        var handCost = Card("OP15-003");
        var lifeTarget = Card("OP16-108");
        me.Characters.Add(source);
        me.Hand.Add(handCost);
        me.Trash.Add(lifeTarget);
        var prompts = new MockPromptService()
            .QueueChoose(handCost.Id.ToString())
            .QueueChoose(lifeTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Same(lifeTarget, Assert.Single(me.LifeArea));
        Assert.True(lifeTarget.IsLifeFaceUp);
        Assert.DoesNotContain(lifeTarget, me.Trash);
        Assert.Contains(handCost, me.Trash);
    }
}
