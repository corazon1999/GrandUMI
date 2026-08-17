using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST13SearchRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("ST13-013", EffectTrigger.OnEnterField)]
    [InlineData("ST13-019", EffectTrigger.EventMain)]
    public async Task BrotherSearches_ExcludeEightCostLuffy(string sourceNumber, EffectTrigger trigger)
    {
        var state = TestScene.New().MyDeckTop("OP17-093", "ST13-014").Build();
        var eightCostLuffy = state.Players[0].Deck[0];
        var lowCostLuffy = state.Players[0].Deck[1];
        var prompts = new MockPromptService().QueueChooseEmpty().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, Card(sourceNumber), trigger, prompts);

        var search = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "LookTopReveal"));
        Assert.DoesNotContain(eightCostLuffy.Id.ToString(), search.choices);
        Assert.Contains(lowCostLuffy.Id.ToString(), search.choices);
    }
}
