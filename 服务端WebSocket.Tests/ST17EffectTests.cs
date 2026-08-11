using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST17EffectTests
{
    [Fact]
    public async Task ST17_003_OnEnterField_ReordersTopThreeToChosenOrder()
    {
        var state = TestScene.New()
            .MyCharacter("ST17-003")
            .MyDeckTop("ST17-001", "ST17-002", "ST17-004", "ST17-005")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(c => c.Info.Number == "ST17-003");
        var first = me.Deck[0];
        var second = me.Deck[1];
        var third = me.Deck[2];
        var fourth = me.Deck[3];
        var prompts = new MockPromptService()
            .QueueChoose(third.Id.ToString(), first.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(new[] { third.Id, first.Id, second.Id, fourth.Id }, me.Deck.Select(c => c.Id));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ST17BuggyReorder", prompt.kind);
        Assert.Contains("自选顺序", prompt.text);
        Assert.Equal(3, prompt.min);
        Assert.Equal(3, prompt.max);
        Assert.Equal(new[] { first.Id.ToString(), second.Id.ToString(), third.Id.ToString() }, prompt.choices);
        Assert.NotNull(prompt.extra);
        Assert.True(prompt.extra!.ContainsKey("choiceCards"));
    }

    [Fact]
    public async Task ST17_003_OnEnterField_WithFewerThanThreeCards_ReordersAvailableCards()
    {
        var state = TestScene.New()
            .MyCharacter("ST17-003")
            .MyDeckTop("ST17-001", "ST17-002")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(c => c.Info.Number == "ST17-003");
        var first = me.Deck[0];
        var second = me.Deck[1];
        var prompts = new MockPromptService()
            .QueueChoose(second.Id.ToString(), first.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Equal(new[] { second.Id, first.Id }, me.Deck.Select(c => c.Id));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(2, prompt.min);
        Assert.Equal(2, prompt.max);
    }

    [Fact]
    public async Task ST17_003_OnEnterField_WithEmptyDeck_DoesNotPrompt()
    {
        var state = TestScene.New()
            .MyCharacter("ST17-003")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(c => c.Info.Number == "ST17-003");
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Empty(prompts.ChooseHistory);
        Assert.Empty(me.Deck);
    }
}
