using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class DrawAgreementTests
{
    private static readonly JsonElement EmptyData = JsonSerializer.SerializeToElement(new { });
    private const string DefaultDescription = "效果结算后无法继续操作";

    [Fact]
    public void 对方同意后以无胜者平局结束()
    {
        var engine = CreateEngine();
        JsonElement player1 = default;
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) player1 = JsonSerializer.SerializeToElement(payload);
        };

        Assert.True(engine.HandleAction(0, "RequestDraw", DrawData()));
        Assert.Equal(0, engine.State.PendingDrawRequester);
        Assert.Equal(DefaultDescription, engine.State.PendingDrawRequestDescription);

        Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = true })));

        Assert.True(engine.State.IsGameOver);
        Assert.True(engine.State.IsDraw);
        Assert.Null(engine.State.WinnerIndex);
        Assert.Null(engine.State.PendingDrawRequester);
        Assert.Null(engine.State.PendingDrawRequestDescription);
        Assert.Equal("双方同意因 Bug 平局", engine.State.GameOverReason);
        Assert.Equal(JsonValueKind.Null, player1.GetProperty("drawRequestDescription").ValueKind);
    }

    [Fact]
    public void 连续被拒绝三次后不能再次申请()
    {
        var engine = CreateEngine();

        for (var attempt = 1; attempt <= GameState.DrawRequestRejectionLimit; attempt++)
        {
            Assert.True(engine.HandleAction(0, "RequestDraw", DrawData($"第 {attempt} 次出现效果卡死")));
            Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = false })));
            Assert.Equal(attempt, engine.State.DrawRequestRejectionCounts[0]);
            Assert.Null(engine.State.PendingDrawRequester);
            Assert.Null(engine.State.PendingDrawRequestDescription);
            Assert.False(engine.State.IsGameOver);
        }

        Assert.False(engine.HandleAction(0, "RequestDraw", DrawData()));
        Assert.Null(engine.State.PendingDrawRequester);
        Assert.Null(engine.State.PendingDrawRequestDescription);
        Assert.Equal(GameState.DrawRequestRejectionLimit, engine.State.DrawRequestRejectionCounts[0]);
    }

    [Fact]
    public void 只有对方可以回应且双方拒绝次数分别计算()
    {
        var engine = CreateEngine();

        Assert.True(engine.HandleAction(1, "RequestDraw", DrawData()));
        Assert.False(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = true })));
        Assert.Equal(1, engine.State.PendingDrawRequester);
        Assert.Equal(DefaultDescription, engine.State.PendingDrawRequestDescription);

        Assert.True(engine.HandleAction(0, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = false })));
        Assert.Equal(0, engine.State.DrawRequestRejectionCounts[0]);
        Assert.Equal(1, engine.State.DrawRequestRejectionCounts[1]);
    }

    [Fact]
    public void 权威快照按玩家视角下发申请状态和次数()
    {
        var engine = CreateEngine();
        JsonElement player0 = default;
        JsonElement player1 = default;
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            var json = JsonSerializer.SerializeToElement(payload);
            if (playerIndex == 0) player0 = json;
            else player1 = json;
        };

        Assert.True(engine.HandleAction(0, "RequestDraw", DrawData("  效果结算后无法选择卡牌  \n")));

        Assert.True(player0.GetProperty("drawRequestPendingFromMe").GetBoolean());
        Assert.False(player0.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
        Assert.False(player1.GetProperty("drawRequestPendingFromMe").GetBoolean());
        Assert.True(player1.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
        Assert.Equal("效果结算后无法选择卡牌", player0.GetProperty("drawRequestDescription").GetString());
        Assert.Equal("效果结算后无法选择卡牌", player1.GetProperty("drawRequestDescription").GetString());
        Assert.Equal(0, player0.GetProperty("drawRequestRejectionCount").GetInt32());
        Assert.Equal(3, player0.GetProperty("drawRequestRejectionLimit").GetInt32());

        var spectator = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, -1));
        Assert.Equal(JsonValueKind.Null, spectator.GetProperty("drawRequestDescription").ValueKind);
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(engine.State));
        Assert.Equal("效果结算后无法选择卡牌",
            privateSnapshot.GetProperty("pendingDrawRequestDescription").GetString());

        Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = false })));
        Assert.Equal(1, player0.GetProperty("drawRequestRejectionCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, player0.GetProperty("drawRequestDescription").ValueKind);
        Assert.Equal(JsonValueKind.Null, player1.GetProperty("drawRequestDescription").ValueKind);
    }

    [Fact]
    public void 描述缺失_空白或非文字时明确拒绝且不改变状态或落盘()
    {
        var engine = CreateEngine();
        var initialTick = engine.State.Tick;
        var persistedActions = 0;
        string? rejectionReason = null;
        engine.OnPersistAction = (_, _, _, _) => persistedActions++;
        engine.OnSendToPlayer = (_, payload) =>
        {
            var json = JsonSerializer.SerializeToElement(payload);
            if (json.TryGetProperty("proto", out var proto)
                && proto.GetString() == "MsgActionRejected")
                rejectionReason = json.GetProperty("reason").GetString();
        };

        Assert.False(engine.HandleAction(0, "RequestDraw", EmptyData));
        Assert.Equal("请填写发生了什么 Bug", rejectionReason);
        AssertDrawRequestUnchanged(engine, initialTick, persistedActions);

        Assert.False(engine.HandleAction(0, "RequestDraw", DrawData(" \r\n\t ")));
        Assert.Equal("请填写发生了什么 Bug", rejectionReason);
        AssertDrawRequestUnchanged(engine, initialTick, persistedActions);

        Assert.False(engine.HandleAction(0, "RequestDraw",
            JsonSerializer.SerializeToElement(new { description = 123 })));
        Assert.Equal("Bug 描述格式无效，请填写文字", rejectionReason);
        AssertDrawRequestUnchanged(engine, initialTick, persistedActions);
    }

    [Fact]
    public void 描述先去除首尾空白再校验五百字符边界()
    {
        var accepted = CreateEngine();
        var boundary = new string('甲', GameState.DrawRequestDescriptionMaxLength);
        Assert.True(accepted.HandleAction(0, "RequestDraw", DrawData($" \n{boundary}\t ")));
        Assert.Equal(boundary, accepted.State.PendingDrawRequestDescription);

        var rejected = CreateEngine();
        string? reason = null;
        rejected.OnSendToPlayer = (_, payload) =>
        {
            var json = JsonSerializer.SerializeToElement(payload);
            if (json.TryGetProperty("reason", out var value)) reason = value.GetString();
        };
        Assert.False(rejected.HandleAction(0, "RequestDraw",
            DrawData(new string('甲', GameState.DrawRequestDescriptionMaxLength + 1))));
        Assert.Equal("Bug 描述不能超过 500 个字符", reason);
        Assert.Null(rejected.State.PendingDrawRequester);
        Assert.Null(rejected.State.PendingDrawRequestDescription);
    }

    [Fact]
    public void 重复申请和无效回应都不能覆盖或清理现有描述()
    {
        var engine = CreateEngine();
        Assert.True(engine.HandleAction(0, "RequestDraw", DrawData("第一份描述")));

        Assert.False(engine.HandleAction(1, "RequestDraw", DrawData("试图覆盖的描述")));
        Assert.Equal(0, engine.State.PendingDrawRequester);
        Assert.Equal("第一份描述", engine.State.PendingDrawRequestDescription);

        Assert.False(engine.HandleAction(1, "RespondDraw",
            JsonSerializer.SerializeToElement(new { accept = "yes" })));
        Assert.Equal(0, engine.State.PendingDrawRequester);
        Assert.Equal("第一份描述", engine.State.PendingDrawRequestDescription);

        Assert.True(engine.HandleAction(1, "RespondDraw",
            JsonSerializer.SerializeToElement(new { accept = false })));
        Assert.True(engine.HandleAction(0, "RequestDraw", DrawData("第二份描述")));
        Assert.Equal("第二份描述", engine.State.PendingDrawRequestDescription);
    }

    [Fact]
    public void 投降或其他终局都会成对清理申请者和描述()
    {
        var surrender = CreateEngine();
        Assert.True(surrender.HandleAction(0, "RequestDraw", DrawData()));
        Assert.True(surrender.HandleAction(1, "Surrender", EmptyData));
        Assert.Null(surrender.State.PendingDrawRequester);
        Assert.Null(surrender.State.PendingDrawRequestDescription);

        var externalTerminal = CreateEngine();
        Assert.True(externalTerminal.HandleAction(0, "RequestDraw", DrawData()));
        externalTerminal.State.WinnerIndex = 1;
        Assert.Null(externalTerminal.State.PendingDrawRequester);
        Assert.Null(externalTerminal.State.PendingDrawRequestDescription);
    }

    [Fact]
    public async Task 动作日志重放会恢复描述并兼容上线前的无描述申请()
    {
        _ = TestScene.New().Build();
        var deck = BuildLegalDeck("OP15-001");
        var rebuilt = await MatchReplay.RebuildAsync(
            "draw-replay",
            seed: 123456,
            firstPlayer: 0,
            ("alice", deck),
            ("bob", deck),
            [MatchReplay.Action(0, "RequestDraw", "{\"description\":\"  重放后仍需展示  \"}")]);

        Assert.Equal(0, rebuilt.State.PendingDrawRequester);
        Assert.Equal("重放后仍需展示", rebuilt.State.PendingDrawRequestDescription);
        var resync = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(rebuilt.State, 1));
        Assert.True(resync.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
        Assert.Equal("重放后仍需展示", resync.GetProperty("drawRequestDescription").GetString());
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(rebuilt.State));
        Assert.Equal("重放后仍需展示",
            privateSnapshot.GetProperty("pendingDrawRequestDescription").GetString());

        var legacy = await MatchReplay.RebuildAsync(
            "draw-replay-legacy",
            seed: 123456,
            firstPlayer: 0,
            ("alice", deck),
            ("bob", deck),
            [MatchReplay.Action(0, "RequestDraw")]);
        Assert.Equal(GameState.LegacyDrawRequestDescription, legacy.State.PendingDrawRequestDescription);

        var corrupt = await MatchReplay.RebuildAsync(
            "draw-replay-corrupt",
            seed: 123456,
            firstPlayer: 0,
            ("alice", deck),
            ("bob", deck),
            [MatchReplay.Action(0, "RequestDraw", "{\"description\":123}")]);
        Assert.Null(corrupt.State.PendingDrawRequester);
        Assert.Null(corrupt.State.PendingDrawRequestDescription);
    }

    [Fact]
    public async Task 平局协商期间暂停棋钟_拒绝后恢复()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var requester = 1 - room.Engine.State.CurrentTurnPlayer;

            GameRoomManager.HandleAction(room.PlayerSessionIds[requester], "RequestDraw", DrawData());
            await WaitUntilAsync(() => room.Engine.State.PendingDrawRequester == requester);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);

            var responder = 1 - requester;
            GameRoomManager.HandleAction(
                room.PlayerSessionIds[responder],
                "RespondDraw",
                JsonSerializer.SerializeToElement(new { accept = false }));
            // 清空申请和拒绝广播都早于房间队列最终恢复棋钟，广播还会短暂启动一次棋钟。
            // 先等 Stop 之后写入的 accepted 提交标记，再在 ClockGate 下确认最终恢复，
            // 避免把动作仍在提交中的中间状态当成完成状态。
            await WaitUntilAsync(() => IsDrawResponseCommittedAndClockRunning(room));
            Assert.Null(room.Engine.State.PendingDrawRequester);
            Assert.Equal(room.Engine.State.CurrentTurnPlayer, room.Engine.State.OperationClockActivePlayer);
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
            if (!string.IsNullOrWhiteSpace(room.MatchLogPath))
            {
                try { File.Delete(room.MatchLogPath); } catch { }
            }
        }
    }

    private static GameEngine CreateEngine()
    {
        _ = TestScene.New().Build();
        var deck = BuildLegalDeck("OP15-001");
        var engine = new GameEngine(
            $"draw-{Guid.NewGuid():N}",
            ("draw-s0", "爱丽丝", deck),
            ("draw-s1", "鲍勃", deck),
            firstPlayer: 0);
        engine.State.MatchKind = MatchKind.Ranked;
        return engine;
    }

    private static JsonElement DrawData(string description = DefaultDescription)
        => JsonSerializer.SerializeToElement(new { description });

    private static void AssertDrawRequestUnchanged(GameEngine engine, int expectedTick, int expectedPersistedActions)
    {
        Assert.Null(engine.State.PendingDrawRequester);
        Assert.Null(engine.State.PendingDrawRequestDescription);
        Assert.Equal(expectedTick, engine.State.Tick);
        Assert.Equal(0, expectedPersistedActions);
    }

    private static GameRoomManager.RoomEntry CreateRankedRoom()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return GameRoomManager.CreateRoom(
            $"draw-clock-s0-{suffix}", $"draw-clock-a-{suffix}", BuildLegalDeck("OP15-001"),
            $"draw-clock-s1-{suffix}", $"draw-clock-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true,
            matchKind: MatchKind.Ranked,
            broadcastInitialState: false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition(), "平局协商状态未在预期时间内更新");
    }

    private static bool IsDrawResponseCommittedAndClockRunning(GameRoomManager.RoomEntry room)
    {
        lock (room.FeedbackEvidenceGate)
        {
            if (!room.RecentFeedbackActions.Any(action =>
                    action.Action == "RespondDraw" && action.Outcome == "accepted"))
                return false;
        }

        lock (room.ClockGate)
        {
            return room.Engine.State.PendingDrawRequester is null
                && room.Engine.State.OperationClockActivePlayer == room.Engine.State.CurrentTurnPlayer;
        }
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
