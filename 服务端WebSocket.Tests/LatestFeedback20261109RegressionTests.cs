using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LatestFeedback20261109RegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP15_009_效果KO使用正确的KO置换窗口()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-009")
            .MyCharacter("OP15-003")
            .Build();
        var me = state.Players[0];
        var victim = me.Characters[1];

        var protectedByKoby = await AtomicOps.KOByEffectAsync(
            state, 0, victim, new MockPromptService().QueueConfirm(true), actingSide: 1);

        Assert.False(protectedByKoby);
        Assert.Contains(victim, me.Characters);
        Assert.Equal(-2000, me.Leader.PowerModThisTurn);
    }

    [Fact]
    public async Task OP18_031_重力刀回卡组底时可以守护我方角色()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        var me = state.Players[0];
        var robin = Card("OP18-031");
        var victim = Card("OP15-003");
        me.Characters.AddRange([robin, victim]);

        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);
        await EffectRuntime.Resolve(state, 1, Card("OP06-058"), EffectTrigger.EventMain, prompts);

        Assert.Contains(victim, me.Characters);
        Assert.DoesNotContain(victim, me.Deck);
        Assert.True(robin.IsTapped);
    }

    [Fact]
    public async Task OP18_031_生命触发回手时也可以守护()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 0;
        var opponent = state.Players[1];
        var robin = Card("OP18-031");
        opponent.Characters.Add(robin);
        var prompts = new MockPromptService().QueueChoose(robin.Id.ToString()).QueueConfirm(true);

        await EffectRuntime.Resolve(
            state, 0, Card("OP01-086"), EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Contains(robin, opponent.Characters);
        Assert.DoesNotContain(robin, opponent.Hand);
        Assert.True(robin.IsTapped);
    }

    [Fact]
    public async Task OP18_117_同名中将只出现一个选择项()
    {
        var state = TestScene.New("OP18-079").Build();
        var me = state.Players[0];
        var first = Card("OP18-098");
        var duplicate = Card("OP18-098");
        me.Deck.Add(first);
        me.Trash.Add(duplicate);
        me.LifeArea.Add(Card("OP15-003"));

        var prompts = new MockPromptService().QueueChoose(first.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP18-117"), EffectTrigger.EventMain, prompts);

        var choice = Assert.Single(prompts.ChooseHistory,
            prompt => prompt.kind == "OwnDeckOrTrashCharacter");
        Assert.Single(choice.choices);
        Assert.Contains(first.Id.ToString(), choice.choices);
        Assert.Contains(first, me.Characters);
        Assert.Contains(duplicate, me.Trash);
    }
}
