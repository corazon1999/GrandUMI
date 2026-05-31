using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class MatchLogTests
{
    [Fact]
    public void GameEngine_LogsInitialShuffleAndPrivateSnapshot()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var events = new List<MatchLogEvent>();

        var engine = new GameEngine(
            "match-log-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 123456);

        engine.OnMatchLog = (kind, actor, payload) => events.Add(new(kind, actor, payload));
        engine.FlushPendingMatchLogs();
        engine.RecordMatchLog("match_start", -1, new { rngSeed = engine.State.RngSeed });
        engine.BroadcastInitialState();

        var randomEvents = events.Where(e => e.Kind == "random_event").ToArray();
        Assert.Equal(2, randomEvents.Length);
        Assert.All(randomEvents, e =>
        {
            using var doc = ToJson(e.Payload);
            var root = doc.RootElement;
            Assert.Equal("shuffle", root.GetProperty("type").GetString());
            Assert.Equal("deck", root.GetProperty("zone").GetString());
            Assert.Equal("initial_setup", root.GetProperty("reason").GetString());
            Assert.Equal(123456, root.GetProperty("rngSeed").GetInt32());
            Assert.Equal(50, root.GetProperty("beforeOrder").GetArrayLength());
            Assert.Equal(50, root.GetProperty("afterOrder").GetArrayLength());
        });

        var privateSnapshot = events.Last(e => e.Kind == "private_snapshot");
        using var snapshotDoc = ToJson(privateSnapshot.Payload);
        var players = snapshotDoc.RootElement.GetProperty("players");
        Assert.Equal(41, players[0].GetProperty("deck").GetArrayLength());
        Assert.Equal(4, players[0].GetProperty("life").GetArrayLength());
        Assert.Equal(41, players[1].GetProperty("deck").GetArrayLength());
        Assert.Equal(4, players[1].GetProperty("life").GetArrayLength());
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var i = 0;
        while (lines.Count < 51)
        {
            var card = pool[i % pool.Count];
            var count = counts.GetValueOrDefault(card.Number, 0);
            if (count < 4)
            {
                lines.Add(card.Number);
                counts[card.Number] = count + 1;
            }
            i++;
        }
        return string.Join('\n', lines);
    }

    private static JsonDocument ToJson(object? payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));

    private sealed record MatchLogEvent(string Kind, int? Actor, object? Payload);
}
