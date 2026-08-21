using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-22 玩家补充的三项卡牌交互回归测试。</summary>
public class Feedback20260822ThreeIssuesTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP11_040_ShowsAllFiveCardsBeforeChoosingStrawHatAndReorderingRest()
    {
        var state = TestScene.New("OP11-040").Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        for (int i = 0; i < 8; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });

        var strawHat = Card("OP01-024");
        var otherCards = new[]
        {
            Card("OP15-003"),
            Card("ST30-010"),
            Card("OP17-022"),
            Card("EB03-057"),
        };
        Assert.True(strawHat.Info.HasKeyword("草帽一伙"));
        Assert.All(otherCards, card => Assert.False(card.Info.HasKeyword("草帽一伙")));
        me.Deck.Add(strawHat);
        me.Deck.AddRange(otherCards);

        var reordered = otherCards.Reverse().ToArray();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(strawHat.Id.ToString())
            .QueueChoose(reordered.Select(card => card.Id.ToString()).ToArray())
            .QueueOption(0);

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnTurnStart, prompts);

        var search = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "LookTopReveal"));
        Assert.Equal(new[] { strawHat.Id.ToString() }, search.choices);
        Assert.NotNull(search.extra);
        using var visibleJson = JsonDocument.Parse(JsonSerializer.Serialize(search.extra!["choiceCards"]));
        var visibleIds = visibleJson.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();
        Assert.Equal(new[] { strawHat.Id.ToString() }.Concat(otherCards.Select(card => card.Id.ToString())), visibleIds);

        var orderPrompt = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "OrderDeckCards"));
        Assert.Contains("自选顺序", orderPrompt.text);

        Assert.Contains(strawHat, me.Hand);
        Assert.Equal(reordered.Select(card => card.Id), me.Deck.Take(4).Select(card => card.Id));
    }

    [Fact]
    public async Task OP17_022_CanRushAfterNormalPlayWithOP06_022Leader()
    {
        var state = TestScene.New("OP06-022")
            .MyActiveDon(10)
            .MyHandAdd("OP17-022")
            .Build();
        state.TurnCount = 3;

        var result = CardPlayer.Play(state, 0, 0);
        await EffectRuntime.Resolve(
            state, 0, result.Card, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains("速攻", result.Card.Info.Abilities);
        var attack = ActionValidator.CanAttack(
            state, 0, result.Card.Id, targetIsLeader: true, targetId: null);
        Assert.True(attack.Ok, attack.Reason);
    }

    [Fact]
    public async Task EB03_057_NormalPlayWithOP06_022LeaderAttachesThreeRestedDon()
    {
        var state = TestScene.New("OP06-022")
            .MyActiveDon(5)
            .MyHandAdd("EB03-057")
            .Build();
        state.TurnCount = 3;
        var me = state.Players[0];

        var result = CardPlayer.Play(state, 0, 0);
        await EffectRuntime.Resolve(
            state, 0, result.Card, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(result.Card, me.Characters);
        Assert.Equal(3, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(2, me.RestDonCount);
    }
}
