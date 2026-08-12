using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Cluster;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class LatencyOptimizationTests
{
    [Fact]
    public async Task WebSocket发送队列_有界且关键消息可淘汰低优先级消息()
    {
        var senderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<string>();
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

        Assert.True(session.Enqueue("gate", null, WsSession.OutboundPriority.Critical));
        await senderEntered.Task;
        for (var i = 0; i < WsSession.MaxOutboundMessages - 1; i++)
            Assert.True(session.Enqueue($"normal-{i}", null, WsSession.OutboundPriority.Normal));
        Assert.True(session.Enqueue("discardable", "presence", WsSession.OutboundPriority.BestEffort));

        Assert.Equal(WsSession.MaxOutboundMessages, session.OutboundDepth);
        Assert.False(session.Enqueue("best-effort-overflow", null, WsSession.OutboundPriority.BestEffort));
        Assert.True(session.Enqueue("critical", null, WsSession.OutboundPriority.Critical));
        Assert.Equal(WsSession.MaxOutboundMessages, session.OutboundDepth);
        Assert.Equal(2, session.DroppedOutboundCount);

        releaseSender.TrySetResult();
        await session.StopSenderAsync();

        Assert.Contains("critical", received);
        Assert.DoesNotContain("discardable", received);
        Assert.DoesNotContain("best-effort-overflow", received);
        Assert.True(session.MaxOutboundDepth <= WsSession.MaxOutboundMessages);
    }

    [Fact]
    public void WebSocket限流_令牌耗尽后拒绝并按桶隔离()
    {
        var session = new WsSession { Socket = null! };

        Assert.True(session.TryConsumeRateLimit("chat", capacity: 2, refillPerSecond: 0));
        Assert.True(session.TryConsumeRateLimit("chat", capacity: 2, refillPerSecond: 0));
        Assert.False(session.TryConsumeRateLimit("chat", capacity: 2, refillPerSecond: 0));
        Assert.True(session.TryConsumeRateLimit("player-list", capacity: 1, refillPerSecond: 0));
        Assert.False(session.TryConsumeRateLimit("player-list", capacity: 1, refillPerSecond: 0));
    }

    [Fact]
    public void 房间目录_注册解析快照与注销保持一致()
    {
        var directory = new LocalRoomPlacementDirectory();
        var roomId = $"placement-{Guid.NewGuid():N}";

        directory.RegisterLocal(roomId);

        Assert.True(directory.TryResolve(roomId, out var placement));
        Assert.Equal(roomId, placement.RoomId);
        Assert.Equal(directory.LocalNodeId, placement.NodeId);
        Assert.Contains(directory.Snapshot(), item => item.RoomId == roomId);

        directory.Unregister(roomId);
        Assert.False(directory.TryResolve(roomId, out _));
    }

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
    public void 含效果发动事件的状态快照_不可被后续普通快照合并()
    {
        var method = typeof(WebSocketBridge).GetMethod(
            "IsReplaceableStateSnapshot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var ordinary = new
        {
            proto = "MsgGameState",
            lastAction = "EffectResolved",
            effectActivations = Array.Empty<object>(),
        };
        var withActivation = new
        {
            proto = "MsgGameState",
            lastAction = "EffectResolved",
            effectActivations = new[] { new { sourceId = "card-1" } },
        };

        Assert.True((bool)method.Invoke(null, new object[] { ordinary })!);
        Assert.False((bool)method.Invoke(null, new object[] { withActivation })!);
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
    public void 增量快照_可按实际发送基线逐字段重建且旧客户端保持完整快照()
    {
        var state = TestScene.MaxScenario();
        state.Tick = 30;
        var first = StateSnapshotBuilder.Build(state, 0, "GameStart", requestId: "req-first");
        var firstEncoded = SnapshotWireCodec.Encode(first, true, null, -1, 0);

        Assert.True(firstEncoded.IsStateSnapshot);
        Assert.False(firstEncoded.IsDelta);
        Assert.NotNull(firstEncoded.NewBaseline);
        var sameSnapshotForAnotherConnection = SnapshotWireCodec.Encode(first, false, null, -1, 0);
        Assert.Same(firstEncoded.Bytes, sameSnapshotForAnotherConnection.Bytes);

        state.Tick = 31;
        state.TurnCount++;
        var second = StateSnapshotBuilder.Build(state, 0, "EndTurn", requestId: "req-second");
        var secondEncoded = SnapshotWireCodec.Encode(
            second,
            true,
            firstEncoded.NewBaseline,
            firstEncoded.Tick,
            firstEncoded.DeltasSinceFull);

        Assert.True(secondEncoded.IsDelta);
        Assert.True(secondEncoded.Bytes.Length < JsonSerializer.SerializeToUtf8Bytes(second).Length);
        using var deltaDocument = JsonDocument.Parse(secondEncoded.Bytes);
        Assert.Equal(30, deltaDocument.RootElement.GetProperty("baseTick").GetInt32());
        var reconstructed = SnapshotWireCodec.ApplyDelta(firstEncoded.NewBaseline!.Value, deltaDocument.RootElement);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(second), reconstructed));

        var legacyEncoded = SnapshotWireCodec.Encode(
            second,
            false,
            firstEncoded.NewBaseline,
            firstEncoded.Tick,
            firstEncoded.DeltasSinceFull);
        Assert.False(legacyEncoded.IsDelta);
        Assert.Equal("MsgGameState", JsonDocument.Parse(legacyEncoded.Bytes).RootElement.GetProperty("proto").GetString());

        var periodicFull = SnapshotWireCodec.Encode(second, true, firstEncoded.NewBaseline, firstEncoded.Tick, 32);
        Assert.False(periodicFull.IsDelta);
    }

    [Fact]
    public void 公开快照_写入对局日志时只物化一次()
    {
        var shared = new SharedJsonValue(new { proto = "MsgGameState", tick = 9, values = Enumerable.Range(1, 20).ToArray() });

        var matchLog = JsonSerializer.Serialize(new { kind = "public_snapshot", payload = shared });

        Assert.Contains("\"tick\":9", matchLog);
        Assert.Equal(1, shared.MaterializationCount);
    }

    [Fact]
    public async Task 房间生命周期_只写统一对局日志且不创建重复录像()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var room = GameRoomManager.CreateRoom(
            $"unified-log-s0-{Guid.NewGuid():N}", "unified-log-alice", deck,
            $"unified-log-s1-{Guid.NewGuid():N}", "unified-log-bob", deck,
            p0First: true);
        var matchLogPath = Assert.IsType<string>(room.MatchLogPath);
        var dateDirectory = Directory.GetParent(matchLogPath)!;
        var matchLogRoot = Directory.GetParent(dateDirectory.FullName)!;
        var dataRoot = Directory.GetParent(matchLogRoot.FullName)!;
        var replayPath = Path.Combine(
            dataRoot.FullName,
            "Replays",
            dateDirectory.Name,
            $"{room.RoomId}.jsonl");

        GameRoomManager.CleanupRoom(room.RoomId);
        for (var i = 0; i < 200 && (!File.Exists(matchLogPath) || IsFileLocked(matchLogPath)); i++)
            await Task.Delay(10);

        try
        {
            Assert.True(File.Exists(matchLogPath));
            Assert.Contains(
                File.ReadLines(matchLogPath),
                line => JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString() == "public_snapshot");
            Assert.False(File.Exists(replayPath));
        }
        finally
        {
            TryDelete(matchLogPath);
        }
    }

    [Fact]
    public void 动作拒绝_回包携带对应请求编号()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var engine = new GameEngine(
            "request-id-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 123456);
        object? response = null;
        engine.OnSendToPlayer = (_, payload) => response = payload;

        Assert.False(engine.HandleAction(1, "EndTurn", JsonSerializer.SerializeToElement(new { }), "req-rejected"));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.Equal("req-rejected", document.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task 房间动作队列_按进入顺序完成先后手选择换牌并结束首回合()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var room = GameRoomManager.CreateRoom(
            "queue-s0", "queue-alice", deck,
            "queue-s1", "queue-bob", deck);

        try
        {
            Assert.NotNull(room.MatchLogPath);
            var chooser = room.Engine.State.StartingPlayerChooser;
            var chooserSid = chooser == 0 ? "queue-s0" : "queue-s1";
            GameRoomManager.HandleAction(chooserSid, "ChooseFirstPlayer", JsonSerializer.SerializeToElement(new { goFirst = true }));
            GameRoomManager.HandleAction("queue-s0", "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            GameRoomManager.HandleAction("queue-s1", "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            GameRoomManager.HandleAction(chooserSid, "EndTurn", JsonSerializer.SerializeToElement(new { }));

            for (var i = 0; i < 500 && room.Engine.State.TurnCount != 2; i++)
                await Task.Delay(2);

            Assert.True(room.Engine.State.StartingPlayerChosen);
            Assert.True(room.Engine.State.MulliganBothDone);
            Assert.Equal(1 - chooser, room.Engine.State.CurrentTurnPlayer);
            Assert.Equal(2, room.Engine.State.TurnCount);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            TryDelete(room.MatchLogPath);
        }
    }

    [Fact]
    public async Task 房间动作队列_拒绝的PromptResponse不会阻塞后续合法响应()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var suffix = Guid.NewGuid().ToString("N");
        var player0Session = $"rejected-prompt-s0-{suffix}";
        var player1Session = $"rejected-prompt-s1-{suffix}";
        var room = GameRoomManager.CreateRoom(
            player0Session, "rejected-prompt-alice", deck,
            player1Session, "rejected-prompt-bob", deck,
            p0First: true);

        try
        {
            var state = room.Engine.State;
            var me = state.Players[0];
            var playable = CardDatabase.GetBySet("OP15")
                .First(c => c.Kind == CardKind.Character && !c.EffectTags.Contains("OnEnterField"));
            var filler = CardDatabase.GetBySet("OP15").First(c => c.Kind == CardKind.Character);

            state.CurrentTurnPlayer = 0;
            state.TurnCount = 2;
            state.Phase = Phase.Main;
            me.Hand.Clear();
            me.Hand.Add(new CardInstance { Info = playable });
            me.Characters.Clear();
            for (var i = 0; i < 5; i++)
                me.Characters.Add(new CardInstance { Info = filler });
            me.Trash.Clear();
            me.CostArea.Clear();
            for (var i = 0; i < 10; i++)
                me.CostArea.Add(new DonCard { State = DonState.Active });

            GameRoomManager.HandleAction(
                player0Session,
                "PlayCard",
                JsonSerializer.SerializeToElement(new { handIndex = 0 }));

            for (var i = 0; i < 300 && state.PendingPrompt?.Kind != "OverflowTrash"; i++)
                await Task.Delay(10);

            var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
            Assert.Equal("OverflowTrash", prompt.Kind);
            var victim = me.Characters[0];
            var emptyResponse = JsonSerializer.SerializeToElement(new
            {
                promptId = prompt.PromptId,
                chosen = Array.Empty<string>(),
            });

            // 引擎必须明确标记这个回包被拒绝，且保留原 Prompt。
            Assert.False(room.Engine.HandleAction(0, "PromptResponse", emptyResponse));
            Assert.Equal(prompt.PromptId, state.PendingPrompt?.PromptId);

            // 再走真实房间队列：非法回包后紧跟合法回包。旧实现会在第一个回包上卡 15 秒。
            GameRoomManager.HandleAction(player0Session, "PromptResponse", emptyResponse);
            GameRoomManager.HandleAction(
                player0Session,
                "PromptResponse",
                JsonSerializer.SerializeToElement(new
                {
                    promptId = prompt.PromptId,
                    chosen = new[] { victim.Id.ToString() },
                }));

            for (var i = 0; i < 300 && (state.PendingPrompt is not null || me.Hand.Count != 0); i++)
                await Task.Delay(10);

            Assert.Null(state.PendingPrompt);
            Assert.Empty(me.Hand);
            Assert.Contains(victim, me.Trash);
            Assert.DoesNotContain(victim, me.Characters);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            TryDelete(room.MatchLogPath);
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

    private static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
