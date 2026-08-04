using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP15_066_SatoriTests
{
    [Fact]
    public async Task OnAttack_WithAtMostSixDon_ReordersTopTwoAndMovesThemToBottom()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-066")
            .MyActiveDon(6)
            .MyDeckTop("OP15-061", "OP15-067", "OP15-068")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(c => c.Info.Number == "OP15-066");
        var first = me.Deck[0];
        var second = me.Deck[1];
        var third = me.Deck[2];
        var prompts = new MockPromptService()
            .QueueChoose(second.Id.ToString(), first.Id.ToString())
            .QueueOption(1);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(third.Id, me.Deck[0].Id);
        Assert.Equal(second.Id, me.Deck[^2].Id);
        Assert.Equal(first.Id, me.Deck[^1].Id);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("DeckReorder", prompt.kind);
        Assert.Equal(2, prompt.min);
        Assert.Equal(2, prompt.max);
    }

    [Fact]
    public async Task OnAttack_WithMoreThanSixDon_DoesNotActivate()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-066")
            .MyActiveDon(7)
            .MyDeckTop("OP15-061", "OP15-067", "OP15-068")
            .Build();
        var me = state.Players[0];
        var source = me.Characters.Single(c => c.Info.Number == "OP15-066");
        var originalOrder = me.Deck.Select(c => c.Id).ToList();
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(originalOrder, me.Deck.Select(c => c.Id));
        Assert.Empty(prompts.ChooseHistory);
    }
}
