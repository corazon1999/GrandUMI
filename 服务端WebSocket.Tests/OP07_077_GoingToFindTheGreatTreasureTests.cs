using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class OP07_077_GoingToFindTheGreatTreasureTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task EventMain_RevealsOnlyTheSelectedCardToOpponent()
    {
        const string deck = "OP17-099\nOP17-100";
        var engine = new GameEngine("op07-077-public-reveal",
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 77);
        var opponentMessages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) opponentMessages.Enqueue(JsonSerializer.Serialize(payload));
        };

        var selected = Card("OP17-106");
        var hiddenRemainder = Card("OP01-001");
        var player = engine.State.Players[0];
        player.Deck.Clear();
        player.Deck.AddRange([selected, hiddenRemainder]);
        var source = Card("OP07-077");

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, source, EffectTrigger.EventMain, engine.Prompts);

        for (var i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal(new[] { selected.Id.ToString() }, prompt.ValidChoices);
        engine.Prompts.Resolve(prompt.PromptId, new[] { selected.Id.ToString() });
        await resolveTask;

        Assert.Contains(selected, player.Hand);
        Assert.DoesNotContain(selected, player.Deck);
        Assert.Equal(new[] { hiddenRemainder }, player.Deck);

        var revealMessages = opponentMessages
            .Select(message => JsonDocument.Parse(message))
            .Where(document => document.RootElement.GetProperty("lastAction").GetString() == "RevealCards")
            .ToList();
        var revealMessage = Assert.Single(revealMessages);
        var revealedNumbers = revealMessage.RootElement
            .GetProperty("reveal")
            .GetProperty("cardNumbers")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();
        Assert.Equal(new[] { selected.Info.Number }, revealedNumbers);
        Assert.DoesNotContain(hiddenRemainder.Info.Number, revealedNumbers);

        foreach (var document in revealMessages) document.Dispose();
    }

    [Fact]
    public async Task EventMain_WhenSelectionIsSkipped_DoesNotBroadcastReveal()
    {
        const string deck = "OP17-099\nOP17-100";
        var engine = new GameEngine("op07-077-skip-reveal",
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 78);
        var opponentMessages = new ConcurrentQueue<string>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) opponentMessages.Enqueue(JsonSerializer.Serialize(payload));
        };

        var candidate = Card("OP17-106");
        var player = engine.State.Players[0];
        player.Deck.Clear();
        player.Deck.Add(candidate);
        var source = Card("OP07-077");

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, source, EffectTrigger.EventMain, engine.Prompts);

        for (var i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);

        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        engine.Prompts.Resolve(prompt.PromptId, Array.Empty<string>());
        await resolveTask;

        Assert.Empty(player.Hand);
        Assert.Contains(candidate, player.Deck);

        var revealCount = opponentMessages
            .Select(message => JsonDocument.Parse(message))
            .Count(document => document.RootElement.GetProperty("lastAction").GetString() == "RevealCards");
        Assert.Equal(0, revealCount);
    }
}
