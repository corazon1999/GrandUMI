using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class EffectActivationSnapshotTests
{
    [Fact]
    public void 效果发动队列_按玩家和观战视角转换来源方并保留顺序()
    {
        var state = TestScene.MaxScenario();
        var mySource = state.Players[0].Leader;
        var opponentSource = state.Players[1].Leader;
        var activations = new[]
        {
            new EffectActivationEvent(0, mySource.Id, mySource.Info.Number, EffectTrigger.ActivatedMain.ToString()),
            new EffectActivationEvent(1, opponentSource.Id, opponentSource.Info.Number, EffectTrigger.OnOppAttackDeclare.ToString()),
        };

        var snapshots = StateSnapshotBuilder.BuildAll(
            state,
            "EffectResolved",
            effectActivations: activations);

        using var player0 = Parse(snapshots.Player0);
        using var player1 = Parse(snapshots.Player1);
        using var spectator = Parse(snapshots.Spectator);

        Assert.Equal(new[] { "my", "opponent" }, Sides(player0));
        Assert.Equal(new[] { "opponent", "my" }, Sides(player1));
        Assert.Equal(new[] { "my", "opponent" }, Sides(spectator));
        Assert.Equal(
            new[] { mySource.Id.ToString(), opponentSource.Id.ToString() },
            SourceIds(player0));
    }

    [Fact]
    public void 效果发动队列_只随下一份快照发送且跳过开局被动()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var engine = new GameEngine(
            "effect-activation-snapshot-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260808);
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };
        var source = engine.State.Players[0].Leader;

        engine.QueueEffectActivation(0, source, EffectTrigger.ActivatedMain);
        engine.Broadcast("EffectResolved");
        engine.Broadcast("Heartbeat");
        engine.QueueEffectActivation(0, source, EffectTrigger.OnGameStart);
        engine.Broadcast("GameStartPassiveRegistered");

        Assert.Equal(3, snapshots.Count);
        Assert.Single(snapshots[0].GetProperty("effectActivations").EnumerateArray());
        Assert.Empty(snapshots[1].GetProperty("effectActivations").EnumerateArray());
        Assert.Empty(snapshots[2].GetProperty("effectActivations").EnumerateArray());
    }

    [Fact]
    public async Task 无对应触发时机的卡进入统一解析入口_不会误报效果发动()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var engine = new GameEngine(
            "effect-activation-no-false-positive-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260808);
        var snapshots = new List<JsonElement>();
        engine.OnSendToPlayer = (index, payload) =>
        {
            if (index == 0) snapshots.Add(JsonSerializer.SerializeToElement(payload));
        };
        var noEnterEffect = CardDatabase.GetBySet("OP15")
            .First(card => card.Kind == CardKind.Character
                && !card.EffectTags.Contains(EffectTrigger.OnEnterField.ToString()));
        var source = new CardInstance { Info = noEnterEffect };

        await EffectRuntime.Resolve(engine.State, 0, source, EffectTrigger.OnEnterField, engine.Prompts);
        engine.Broadcast("EffectResolved");

        var snapshot = Assert.Single(snapshots);
        Assert.Empty(snapshot.GetProperty("effectActivations").EnumerateArray());
    }

    private static JsonDocument Parse(object snapshot)
        => JsonDocument.Parse(JsonSerializer.Serialize(snapshot));

    private static string[] Sides(JsonDocument document)
        => document.RootElement.GetProperty("effectActivations")
            .EnumerateArray()
            .Select(item => item.GetProperty("side").GetString()!)
            .ToArray();

    private static string[] SourceIds(JsonDocument document)
        => document.RootElement.GetProperty("effectActivations")
            .EnumerateArray()
            .Select(item => item.GetProperty("sourceId").GetString()!)
            .ToArray();

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            var count = counts.GetValueOrDefault(card.Number);
            if (count >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = count + 1;
        }
        return string.Join('\n', lines);
    }
}
