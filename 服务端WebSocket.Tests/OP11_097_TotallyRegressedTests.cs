using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP11_097_TotallyRegressedTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EventCounter_WithTenTrash_RecoversLowCostBlackCharacter()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP11-097");
        var validBlackCharacter = Card("OP14-083");
        me.Trash.Add(source);
        me.Trash.Add(validBlackCharacter);
        for (var i = 0; i < 8; i++)
            me.Trash.Add(Card("OP14-090"));
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(validBlackCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(1000, me.Leader.PowerModThisBattle);
        Assert.Contains(validBlackCharacter, me.Hand);
        Assert.DoesNotContain(validBlackCharacter, me.Trash);
        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Contains(validBlackCharacter.Id.ToString(), prompts.ChooseHistory[1].choices);
    }

    [Fact]
    public async Task EventCounter_DoesNotOfferWrongColorOrOverCostCharacters()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP11-097");
        var purpleCharacter = Card("OP05-062");
        var overCostBlackCharacter = Card("OP14-120");
        me.Trash.Add(source);
        me.Trash.Add(purpleCharacter);
        me.Trash.Add(overCostBlackCharacter);
        for (var i = 0; i < 7; i++)
            me.Trash.Add(Card("OP14-099"));
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Empty(me.Hand);
        Assert.Contains(purpleCharacter, me.Trash);
        Assert.Contains(overCostBlackCharacter, me.Trash);
        Assert.Single(prompts.ChooseHistory);
    }

    [Fact]
    public async Task EventCounter_WithFewerThanTenTrash_DoesNotRecoverCharacter()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP11-097");
        var validBlackCharacter = Card("OP14-083");
        me.Trash.Add(source);
        me.Trash.Add(validBlackCharacter);
        for (var i = 0; i < 7; i++)
            me.Trash.Add(Card("OP14-099"));
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(9, me.Trash.Count);
        Assert.Empty(me.Hand);
        Assert.Contains(validBlackCharacter, me.Trash);
        Assert.Single(prompts.ChooseHistory);
    }
}
