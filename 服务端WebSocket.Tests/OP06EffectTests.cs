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

    [Fact]
    public async Task OP06_117_同时KO时_OP17_095一次支付保护整批目标()
    {
        var state = TestScene.New(myLeaderNumber: "OP05-098").Build();
        var attacker = state.Players[0];
        var defender = state.Players[1];
        var arkMaxim = Card("OP06-117");
        attacker.StageCard = arkMaxim;

        var zoro = Card("OP17-095");
        var otherVictim = Card("OP17-094");
        defender.Characters.AddRange([zoro, otherVictim]);
        var returned = new[] { Card("ST30-002"), Card("ST30-003"), Card("ST30-004") };
        defender.Trash.AddRange(returned);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(attacker.Leader.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(returned.Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, arkMaxim, EffectTrigger.ActivatedMain, prompts);

        Assert.True(arkMaxim.IsTapped);
        Assert.True(attacker.Leader.IsTapped);
        Assert.Contains(zoro, defender.Characters);
        Assert.Contains(otherVictim, defender.Characters);
        Assert.Empty(defender.Trash);
        Assert.Equal(returned, defender.Deck);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
    }
}
