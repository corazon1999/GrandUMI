using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class StartingPlayerFlowTests
{
    [Fact]
    public void 骰点开局_结果合法且点数较大者获得选择权()
    {
        var engine = CreateEngine(firstPlayer: -1, seed: 20260806);

        Assert.False(engine.State.StartingPlayerChosen);
        Assert.Equal(-1, engine.State.FirstPlayer);
        var deadline = Assert.IsType<DateTime>(engine.State.StartingPlayerChoiceDeadlineUtc);
        Assert.InRange(
            deadline,
            DateTime.UtcNow.AddSeconds(GameEngine.StartingPlayerChoiceTimeoutSeconds - 2),
            DateTime.UtcNow.AddSeconds(GameEngine.StartingPlayerChoiceTimeoutSeconds + 2));
        Assert.NotEmpty(engine.State.StartingDiceRounds);
        Assert.All(engine.State.StartingDiceRounds, round =>
        {
            Assert.InRange(round.Player0, 1, 6);
            Assert.InRange(round.Player1, 1, 6);
        });

        var final = engine.State.StartingDiceRounds[^1];
        Assert.NotEqual(final.Player0, final.Player1);
        Assert.Equal(final.Player0 > final.Player1 ? 0 : 1, engine.State.StartingPlayerChooser);
    }

    [Fact]
    public void 骰点开局_同一种子可重现全部骰点轮次()
    {
        var first = CreateEngine(firstPlayer: -1, seed: 314159);
        var second = CreateEngine(firstPlayer: -1, seed: 314159);

        Assert.Equal(first.State.StartingDiceRounds, second.State.StartingDiceRounds);
        Assert.Equal(first.State.StartingPlayerChooser, second.State.StartingPlayerChooser);
    }

    [Fact]
    public void 骰点开局_同点会保留该轮并自动重骰()
    {
        var engine = CreateEngine(firstPlayer: -1, seed: 14);

        Assert.True(engine.State.StartingDiceRounds.Count >= 2);
        Assert.Equal(
            engine.State.StartingDiceRounds[0].Player0,
            engine.State.StartingDiceRounds[0].Player1);
        Assert.NotEqual(
            engine.State.StartingDiceRounds[^1].Player0,
            engine.State.StartingDiceRounds[^1].Player1);
    }

    [Fact]
    public void 骰点开局_仅胜者可选择且选择后才允许调度手牌()
    {
        var engine = CreateEngine(firstPlayer: -1, seed: 271828);
        var chooser = engine.State.StartingPlayerChooser;
        var loser = 1 - chooser;
        var rejected = new List<(int Player, string Reason)>();
        engine.OnSendToPlayer = (player, payload) =>
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            if (document.RootElement.TryGetProperty("proto", out var proto)
                && proto.GetString() == "MsgActionRejected")
                rejected.Add((player, document.RootElement.GetProperty("reason").GetString() ?? ""));
        };

        engine.HandleAction(chooser, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        Assert.False(engine.State.Players[chooser].MulliganDone);
        Assert.Contains(rejected, item => item.Player == chooser && item.Reason.Contains("先后手"));

        engine.HandleAction(loser, "ChooseFirstPlayer", JsonSerializer.SerializeToElement(new { goFirst = true }));
        Assert.False(engine.State.StartingPlayerChosen);
        Assert.Contains(rejected, item => item.Player == loser && item.Reason.Contains("骰点胜者"));

        engine.HandleAction(chooser, "ChooseFirstPlayer", JsonSerializer.SerializeToElement(new { goFirst = false }));
        Assert.True(engine.State.StartingPlayerChosen);
        Assert.Null(engine.State.StartingPlayerChoiceDeadlineUtc);
        Assert.Equal(loser, engine.State.FirstPlayer);
        Assert.Equal(loser, engine.State.CurrentTurnPlayer);

        engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        Assert.True(engine.State.MulliganBothDone);
        Assert.Equal(1, engine.State.TurnCount);
        Assert.Equal(loser, engine.State.CurrentTurnPlayer);
    }

    [Fact]
    public void 预设先后手开局_跳过骰点并保留原调度手牌流程()
    {
        var engine = CreateEngine(firstPlayer: 1, seed: 1234);

        Assert.True(engine.State.StartingPlayerChosen);
        Assert.Empty(engine.State.StartingDiceRounds);
        Assert.Equal(1, engine.State.FirstPlayer);

        engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        Assert.True(engine.State.MulliganBothDone);
        Assert.Equal(1, engine.State.CurrentTurnPlayer);
    }

    [Fact]
    public void 双方确认起手牌后_保留与重抽玩家都维持五张手牌()
    {
        var engine = CreateEngine(firstPlayer: 0, seed: 20260817);

        Assert.All(engine.State.Players, player => Assert.Equal(5, player.Hand.Count));
        engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = true }));
        engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));

        Assert.True(engine.State.MulliganBothDone);
        Assert.All(engine.State.Players, player => Assert.Equal(5, player.Hand.Count));
        Assert.All(engine.State.StartingHandCardNumbers, cards => Assert.Equal(5, cards.Count));

        using var player0 = JsonDocument.Parse(JsonSerializer.Serialize(
            StateSnapshotBuilder.Build(engine.State, 0, "MulliganComplete")));
        Assert.Equal(5, player0.RootElement.GetProperty("my").GetProperty("handCount").GetInt32());
        Assert.Equal(5, player0.RootElement.GetProperty("my").GetProperty("handCardIds").GetArrayLength());
    }

    [Fact]
    public void 调度超时_未决定双方自动保留并进入第一回合()
    {
        var engine = CreateEngine(firstPlayer: 0, seed: 20260807);
        var deadline = Assert.IsType<DateTime>(engine.State.MulliganDeadlineUtc);

        var autoKept = engine.AutoKeepMulligans(deadline);

        Assert.Equal(new[] { 0, 1 }, autoKept);
        Assert.True(engine.State.MulliganBothDone);
        Assert.Equal(1, engine.State.TurnCount);
        Assert.Null(engine.State.MulliganDeadlineUtc);
        Assert.All(engine.State.Players, player => Assert.True(player.HasReDraw));
    }

    [Fact]
    public void 调度超时_不会覆盖已自行选择更换手牌的玩家()
    {
        var engine = CreateEngine(firstPlayer: 0, seed: 20260808);
        var deadline = Assert.IsType<DateTime>(engine.State.MulliganDeadlineUtc);

        engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = true }));
        var autoKept = engine.AutoKeepMulligans(deadline);

        Assert.Equal(new[] { 1 }, autoKept);
        Assert.False(engine.State.Players[0].HasReDraw);
        Assert.True(engine.State.Players[1].HasReDraw);
        Assert.True(engine.State.MulliganBothDone);
    }

    [Fact]
    public void 开局快照_按玩家视角映射骰点和选择权()
    {
        var engine = CreateEngine(firstPlayer: -1, seed: 424242);
        var snapshots = StateSnapshotBuilder.BuildAll(engine.State, "GameStart");
        using var player0 = JsonDocument.Parse(JsonSerializer.Serialize(snapshots.Player0));
        using var player1 = JsonDocument.Parse(JsonSerializer.Serialize(snapshots.Player1));
        using var spectator = JsonDocument.Parse(JsonSerializer.Serialize(snapshots.Spectator));

        var p0Root = player0.RootElement;
        var p1Root = player1.RootElement;
        var firstRound = engine.State.StartingDiceRounds[0];
        Assert.Equal(firstRound.Player0, p0Root.GetProperty("startingDiceRolls")[0].GetProperty("my").GetInt32());
        Assert.Equal(firstRound.Player1, p1Root.GetProperty("startingDiceRolls")[0].GetProperty("my").GetInt32());
        Assert.Equal(engine.State.StartingPlayerChooser == 0, p0Root.GetProperty("canChooseFirstPlayer").GetBoolean());
        Assert.Equal(engine.State.StartingPlayerChooser == 1, p1Root.GetProperty("canChooseFirstPlayer").GetBoolean());
        Assert.Equal(
            engine.State.StartingPlayerChoiceDeadlineUtc,
            p0Root.GetProperty("startingPlayerChoiceDeadlineUtc").GetDateTime());
        Assert.False(spectator.RootElement.GetProperty("canChooseFirstPlayer").GetBoolean());
    }

    [Fact]
    public async Task 骰点选择动作_可通过原始种子和动作磁带完整重建()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        const int seed = 98765;
        var original = new GameEngine(
            "starting-player-original",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: -1,
            rngSeed: seed);
        var chooser = original.State.StartingPlayerChooser;
        var actions = new[]
        {
            MatchReplay.Action(chooser, "ChooseFirstPlayer", "{\"goFirst\":false}"),
            MatchReplay.Action(0, "Mulligan", "{\"redraw\":false}"),
            MatchReplay.Action(1, "Mulligan", "{\"redraw\":false}"),
        };

        foreach (var action in actions)
        {
            original.HandleAction(action.PlayerIndex, action.Action, action.Data);
            await original.WaitSettledAsync();
        }

        var rebuilt = await MatchReplay.RebuildAsync(
            "starting-player-rebuilt",
            seed,
            firstPlayer: -1,
            ("alice", deck),
            ("bob", deck),
            actions);

        Assert.Equal(original.State.StartingDiceRounds, rebuilt.State.StartingDiceRounds);
        Assert.Equal(original.State.StartingPlayerChooser, rebuilt.State.StartingPlayerChooser);
        Assert.Equal(original.State.FirstPlayer, rebuilt.State.FirstPlayer);
        Assert.Equal(original.State.CurrentTurnPlayer, rebuilt.State.CurrentTurnPlayer);
        Assert.Equal(original.State.MulliganBothDone, rebuilt.State.MulliganBothDone);
    }

    private static GameEngine CreateEngine(int firstPlayer, int seed)
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        return new GameEngine(
            $"starting-player-{seed}",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer,
            rngSeed: seed);
    }

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
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }
}
