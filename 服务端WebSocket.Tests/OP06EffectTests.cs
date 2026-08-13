using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP06EffectTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP06_050_SearchExcludesAllCardsNamedTashigi()
    {
        var state = TestScene.New().Build();
        var sameNumberTashigi = Card("OP06-050");
        var otherTashigi = Card("ST06-006");
        var otherNavyCard = Card("OP06-051");
        state.Players[0].Deck.AddRange([sameNumberTashigi, otherTashigi, otherNavyCard]);
        var prompts = new MockPromptService()
            .QueueChooseEmpty()
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(
            state, 0, Card("OP06-050"), EffectTrigger.OnEnterField, prompts);

        var search = Assert.Single(prompts.ChooseHistory.Where(prompt => prompt.kind == "LookTopReveal"));
        Assert.DoesNotContain(sameNumberTashigi.Id.ToString(), search.choices);
        Assert.DoesNotContain(otherTashigi.Id.ToString(), search.choices);
        Assert.Contains(otherNavyCard.Id.ToString(), search.choices);
    }
}
