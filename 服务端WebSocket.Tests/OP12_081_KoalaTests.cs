using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class OP12_081_KoalaTests
{
    [Fact]
    public async Task 攻击对方领袖时_两张当前费用八的角色会抽一张牌()
    {
        var state = TestScene.New("OP12-081")
            .MyCharacter("OP12-087")
            .MyCharacter("OP12-087")
            .MyDeckTop("OP15-003")
            .Build();
        var me = state.Players[0];
        var prompts = new MockPromptService();

        foreach (var robin in me.Characters)
            await EffectRuntime.Resolve(state, 0, robin, EffectTrigger.OnEnterField, prompts);

        Assert.All(me.Characters, robin => Assert.True(state.CurrentCostOf(0, robin) >= 8));
        Assert.All(me.Characters, robin => Assert.True(robin.Info.Cost < 8));

        BattleEngine.StartAttack(state, me.Leader.Id, targetIsLeader: true, targetId: null);
        await BattleEngine.TriggerAttackDeclareAsync(state, prompts);

        Assert.Single(me.Hand);
        Assert.Empty(me.Deck);
    }
}
