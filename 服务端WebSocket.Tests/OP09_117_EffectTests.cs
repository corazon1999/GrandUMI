using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP09_117_EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EventMain_AddsUpToTwoNonSelfCardsWithTriggerToHand()
    {
        var state = TestScene.New().Build();
        var player = state.Players[0];
        var firstEligible = Card("OP09-114");
        var secondEligible = Card("OP09-115");
        var sameName = Card("OP09-117");
        var noTrigger = Card("OP09-118");
        var unselectedEligible = Card("OP09-116");
        player.Deck.AddRange([
            firstEligible,
            secondEligible,
            sameName,
            noTrigger,
            unselectedEligible,
        ]);
        var prompts = new MockPromptService()
            .QueueChoose(firstEligible.Id.ToString(), secondEligible.Id.ToString())
            .QueueChoose(unselectedEligible.Id.ToString(), noTrigger.Id.ToString(), sameName.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP09-117"), EffectTrigger.EventMain, prompts);

        var prompt = prompts.ChooseHistory[0];
        Assert.Equal("LookTopReveal", prompt.kind);
        Assert.Equal(0, prompt.min);
        Assert.Equal(2, prompt.max);
        Assert.Equal(
            new[] { firstEligible.Id.ToString(), secondEligible.Id.ToString(), unselectedEligible.Id.ToString() },
            prompt.choices);
        var reorder = prompts.ChooseHistory[1];
        Assert.Equal("ReorderToDeckBottom", reorder.kind);
        Assert.Equal(0, reorder.min);
        Assert.Equal(3, reorder.max);
        Assert.True(Assert.IsType<bool>(reorder.extra!["allowDefaultOrder"]));
        Assert.Equal(new[] { firstEligible, secondEligible }, player.Hand);
        Assert.Equal(new[] { unselectedEligible, noTrigger, sameName }, player.Deck);
    }
}
