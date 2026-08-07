using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class OP17_050_CreamPowderTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OnEnterField_ConfirmsDeckTopPrivatelyAndDoesNotRevealDrawnCardToOpponent()
    {
        const string deck = "OP17-039\nOP17-040";
        var engine = new GameEngine("op17-050-private-peek",
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 50);
        var opponentMessages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) opponentMessages.Enqueue(JsonSerializer.Serialize(payload));
        };

        var first = Card("OP17-040");
        var second = Card("OP17-044");
        var third = Card("OP17-050");
        var source = Card("OP17-050");
        var player = engine.State.Players[0];
        player.Deck.Clear();
        player.Deck.AddRange([first, second, third]);
        player.Characters.Add(source);

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, source, EffectTrigger.OnEnterField, engine.Prompts);

        for (var i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var orderPrompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal(0, orderPrompt.PlayerIndex);
        Assert.Equal("OrderDeckTop", orderPrompt.Kind);
        Assert.Equal(
            new[] { first.Id.ToString(), second.Id.ToString() },
            orderPrompt.ValidChoices);
        using (var choiceCards = JsonDocument.Parse(JsonSerializer.Serialize(orderPrompt.Extra["choiceCards"])))
        {
            var displayedNumbers = choiceCards.RootElement
                .EnumerateArray()
                .Select(item => item.GetProperty("number").GetString())
                .ToList();
            Assert.Equal(new[] { first.Info.Number, second.Info.Number }, displayedNumbers);
        }

        engine.Prompts.Resolve(orderPrompt.PromptId,
            new[] { second.Id.ToString(), first.Id.ToString() });

        for (var i = 0; i < 100
            && (engine.State.PendingPrompt is null
                || engine.State.PendingPrompt.PromptId == orderPrompt.PromptId); i++)
            await Task.Delay(10);

        var wherePrompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal("Option", wherePrompt.Kind);
        engine.Prompts.Resolve(wherePrompt.PromptId, new[] { "0" });
        await resolveTask;

        Assert.Contains(second, player.Hand);
        Assert.Equal(new[] { first, third }, player.Deck);

        var revealCount = opponentMessages
            .Select(message => JsonDocument.Parse(message))
            .Count(document => document.RootElement.GetProperty("lastAction").GetString() == "RevealCards");
        Assert.Equal(0, revealCount);
    }
}
