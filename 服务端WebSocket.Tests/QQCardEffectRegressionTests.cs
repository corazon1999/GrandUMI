using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class QQCardEffectRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP14_037_EventMain_OnlyOffersRestingCharactersWithOriginalPowerAtMost7000()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var me = state.Players[0];
        var eligible = Card("EB01-002");
        var loki = Card("OP17-119");
        eligible.IsTapped = true;
        loki.IsTapped = true;
        state.Players[1].Characters.AddRange([eligible, loki]);
        var prompts = new MockPromptService()
            .QueueChoose(me.CostArea.Select(don => don.Id.ToString()).ToArray())
            .QueueChoose(loki.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-037"), EffectTrigger.EventMain, prompts);

        var targetPrompt = Assert.Single(
            prompts.ChooseHistory,
            prompt => prompt.kind == "OpponentRestingCharacter");
        Assert.Contains(eligible.Id.ToString(), targetPrompt.choices);
        Assert.DoesNotContain(loki.Id.ToString(), targetPrompt.choices);
        Assert.Contains(loki, state.Players[1].Characters);
        Assert.DoesNotContain(loki, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP12_041_ActivatedMain_ReturnsDonBeforeChoosingMainEventAndExcludesCounterOnlyEvents()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var me = state.Players[0];
        var don = me.CostArea.Single();
        var mainEvent = Card("EB03-060");
        var counterOnlyEvent = Card("EB01-009");
        me.Hand.AddRange([mainEvent, counterOnlyEvent]);
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card("OP12-041"), EffectTrigger.ActivatedMain, prompts);

        Assert.Equal("ReturnOwnDon", prompts.ChooseHistory[0].kind);
        var eventPrompt = Assert.Single(prompts.ChooseHistory, prompt => prompt.kind == "OwnHandEvent");
        Assert.Contains(mainEvent.Id.ToString(), eventPrompt.choices);
        Assert.DoesNotContain(counterOnlyEvent.Id.ToString(), eventPrompt.choices);
        Assert.Empty(me.CostArea);
        Assert.Contains(don, me.DonDeck);
        Assert.Contains(me.TurnOnceUsed, key => key.StartsWith("OP12-041-Activated:", StringComparison.Ordinal));
    }
}
