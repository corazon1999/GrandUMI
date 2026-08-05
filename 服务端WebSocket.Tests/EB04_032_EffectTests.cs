using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>EB04-032 昆因登场效果的回归测试。</summary>
public class EB04_032_EffectTests
{
    [Fact]
    public async Task OnEnterField_DiscardsBeastsPiratesCard_ThenDrawsTwo()
    {
        var state = TestScene.New()
            .MyCharacter("EB04-032")
            .MyHandAdd("ST04-002")
            .MyHandAdd("OP15-003")
            .MyDeckTop("OP15-003", "OP15-003")
            .Build();
        var me = state.Players[0];
        var queen = me.Characters[0];
        var eligible = me.Hand[0];
        var prompts = new MockPromptService().QueueChoose(eligible.Id.ToString());

        await EffectRuntime.Resolve(state, 0, queen, EffectTrigger.OnEnterField, prompts);

        var discardPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("DiscardOwnChosen", discardPrompt.kind);
        Assert.Equal(0, discardPrompt.min);
        Assert.Equal(1, discardPrompt.max);
        Assert.Equal(new[] { eligible.Id.ToString() }, discardPrompt.choices);
        Assert.Contains(eligible, me.Trash);
        Assert.Equal(3, me.Hand.Count);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task OnEnterField_WhenPlayerDeclines_DoesNotDiscardOrDraw()
    {
        var state = TestScene.New()
            .MyCharacter("EB04-032")
            .MyHandAdd("ST04-002")
            .MyDeckTop("OP15-003", "OP15-003")
            .Build();
        var me = state.Players[0];
        var queen = me.Characters[0];
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, queen, EffectTrigger.OnEnterField, prompts);

        Assert.Single(prompts.ChooseHistory);
        Assert.Single(me.Hand);
        Assert.Empty(me.Trash);
        Assert.Equal(2, me.Deck.Count);
    }

    [Fact]
    public async Task OnEnterField_WithoutEligibleCard_DoesNotPromptOrDraw()
    {
        var state = TestScene.New()
            .MyCharacter("EB04-032")
            .MyHandAdd("OP15-003")
            .MyDeckTop("OP15-003", "OP15-003")
            .Build();
        var me = state.Players[0];
        var queen = me.Characters[0];
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, queen, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(prompts.ChooseHistory);
        Assert.Single(me.Hand);
        Assert.Empty(me.Trash);
        Assert.Equal(2, me.Deck.Count);
    }
}
