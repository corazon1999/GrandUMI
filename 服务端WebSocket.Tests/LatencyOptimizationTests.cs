using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class LatencyOptimizationTests
{
    [Fact]
    public async Task WebSocket发送队列_只合并连续普通快照且保持控制消息顺序()
    {
        var received = new List<string>();
        var senderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new WsSession { Socket = null! };
        session.StartSender(async message =>
        {
            var value = (string)message.Data;
            received.Add(value);
            if (value == "gate")
            {
                senderEntered.TrySetResult();
                await releaseSender.Task;
            }
        });

        session.Enqueue("gate", isStateSnapshot: false);
        await senderEntered.Task;
        session.Enqueue("state-1", isStateSnapshot: true);
        session.Enqueue("state-2", isStateSnapshot: true);
        session.Enqueue("prompt", isStateSnapshot: false);
        session.Enqueue("state-3", isStateSnapshot: true);
        session.Enqueue("state-4", isStateSnapshot: true);
        releaseSender.TrySetResult();
        await session.StopSenderAsync();

        Assert.Equal(new[] { "gate", "state-2", "prompt", "state-4" }, received);
        Assert.Equal(2, session.MergedStateCount);
    }

    [Fact]
    public void BuildAll_与单视角快照逐字节等价且观战不泄露Prompt()
    {
        var state = TestScene.MaxScenario();
        state.Tick = 17;
        state.PendingPrompt = new PendingPrompt
        {
            PromptId = "secret-prompt",
            PlayerIndex = 0,
            Kind = "OwnHand",
            PromptText = "隐藏选择",
            ValidChoices = new List<string> { state.Players[0].Hand[0].Id.ToString() },
            MinChoose = 1,
            MaxChoose = 1,
            Extra = new Dictionary<string, object?>(),
        };
        var payload = new { value = 7 };

        var all = StateSnapshotBuilder.BuildAll(state, "Prompt", payload);
        Assert.Equal(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 0, "Prompt", payload)), JsonSerializer.Serialize(all.Player0));
        Assert.Equal(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, 1, "Prompt", payload)), JsonSerializer.Serialize(all.Player1));
        Assert.Equal(JsonSerializer.Serialize(StateSnapshotBuilder.Build(state, -1, "Prompt", payload)), JsonSerializer.Serialize(all.Spectator));

        using var spectator = JsonDocument.Parse(JsonSerializer.Serialize(all.Spectator));
        Assert.Equal(JsonValueKind.Null, spectator.RootElement.GetProperty("pendingPrompt").ValueKind);
    }

    [Fact]
    public async Task 房间动作队列_按进入顺序完成换牌并结束首回合()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var room = GameRoomManager.CreateRoom(
            "queue-s0", "queue-alice", deck,
            "queue-s1", "queue-bob", deck,
            p0First: true);

        try
        {
            GameRoomManager.HandleAction("queue-s0", "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            GameRoomManager.HandleAction("queue-s1", "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            GameRoomManager.HandleAction("queue-s0", "EndTurn", JsonSerializer.SerializeToElement(new { }));

            for (var i = 0; i < 500 && room.Engine.State.CurrentTurnPlayer != 1; i++)
                await Task.Delay(2);

            Assert.True(room.Engine.State.MulliganBothDone);
            Assert.Equal(1, room.Engine.State.CurrentTurnPlayer);
            Assert.Equal(2, room.Engine.State.TurnCount);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            TryDelete(room.ReplayPath);
            TryDelete(room.MatchLogPath);
        }
    }

    [Fact]
    public void ReplayRecorder_Close会按顺序排空后台队列()
    {
        var roomId = $"async-log-test-{Guid.NewGuid():N}";
        var path = ReplayRecorder.Open(roomId);

        try
        {
            for (var i = 0; i < 50; i++)
                ReplayRecorder.Append(roomId, new { index = i });

            ReplayRecorder.Close(roomId);

            var indexes = File.ReadLines(path)
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("index").GetInt32())
                .ToArray();
            Assert.Equal(Enumerable.Range(0, 50), indexes);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void 关闭私有快照后仍保留公开快照()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var events = new List<string>();
        var engine = new GameEngine(
            "private-snapshot-off-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 123456)
        {
            EnablePrivateSnapshotLog = false,
        };

        engine.OnMatchLog = (kind, _, _) => events.Add(kind);
        engine.FlushPendingMatchLogs();
        events.Clear();
        engine.BroadcastInitialState();

        Assert.Contains("public_snapshot", events);
        Assert.DoesNotContain("private_snapshot", events);
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
            var card = pool[i++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { }
    }
}
