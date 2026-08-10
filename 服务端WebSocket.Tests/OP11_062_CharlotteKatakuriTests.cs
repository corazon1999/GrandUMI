using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP11_062_CharlotteKatakuriTests
{
    [Theory]
    [InlineData(EffectTrigger.OnAttackDeclare)]
    [InlineData(EffectTrigger.OnOppAttackDeclare)]
    public async Task AttackTrigger_AfterPayingDon_PrivatelyShowsOpponentDeckTopAndBuffsLeader(
        EffectTrigger trigger)
    {
        var state = TestScene.New("OP11-062")
            .MyActiveDon(1)
            .Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var top = new CardInstance { Info = CardDatabase.Get("OP11-063")! };
        var second = new CardInstance { Info = CardDatabase.Get("OP11-064")! };
        opponent.Deck.Add(top);
        opponent.Deck.Add(second);
        var originalOrder = opponent.Deck.Select(card => card.Id).ToArray();
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, me.Leader, trigger, prompts);

        Assert.Equal(1000, me.Leader.PowerModThisBattle);
        Assert.Empty(me.CostArea);
        Assert.Single(me.DonDeck);
        Assert.Equal(originalOrder, opponent.Deck.Select(card => card.Id));

        Assert.Equal(2, prompts.ChooseHistory.Count);
        Assert.Equal("ReturnOwnDon", prompts.ChooseHistory[0].kind);
        var peekPrompt = prompts.ChooseHistory[1];
        Assert.Equal("LookOppTop", peekPrompt.kind);
        Assert.Empty(peekPrompt.choices);
        Assert.Equal(0, peekPrompt.min);
        Assert.Equal(0, peekPrompt.max);

        using var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(peekPrompt.extra!["choiceCards"]));
        var shown = Assert.Single(choiceCards.RootElement.EnumerateArray());
        Assert.Equal(top.Id.ToString(), shown.GetProperty("id").GetString());
        Assert.Equal(top.Info.Number, shown.GetProperty("number").GetString());
    }

    [Fact]
    public async Task AttackTrigger_CanOnlyBeUsedOncePerTurn()
    {
        var state = TestScene.New("OP11-062")
            .MyActiveDon(2)
            .Build();
        var me = state.Players[0];
        state.Players[1].Deck.Add(new CardInstance { Info = CardDatabase.Get("OP11-063")! });
        var prompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);
        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnOppAttackDeclare, prompts);

        Assert.Equal(1000, me.Leader.PowerModThisBattle);
        Assert.Single(me.CostArea);
        Assert.Single(me.DonDeck);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Equal(2, prompts.ChooseHistory.Count);
    }

    [Fact]
    public async Task AttackTrigger_WhenDeclined_DoesNotPayDonShowDeckTopOrBuffLeader()
    {
        var state = TestScene.New("OP11-062")
            .MyActiveDon(1)
            .Build();
        var me = state.Players[0];
        state.Players[1].Deck.Add(new CardInstance { Info = CardDatabase.Get("OP11-063")! });
        var prompts = new MockPromptService().QueueConfirm(false);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(0, me.Leader.PowerModThisBattle);
        Assert.Single(me.CostArea);
        Assert.Empty(me.DonDeck);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Single(prompts.ConfirmHistory);
    }
}
