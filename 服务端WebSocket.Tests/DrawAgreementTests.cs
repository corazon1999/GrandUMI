using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class DrawAgreementTests
{
    private static readonly JsonElement EmptyData = JsonSerializer.SerializeToElement(new { });

    [Fact]
    public void 对方同意后以无胜者平局结束()
    {
        var engine = CreateEngine();

        Assert.True(engine.HandleAction(0, "RequestDraw", EmptyData));
        Assert.Equal(0, engine.State.PendingDrawRequester);

        Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = true })));

        Assert.True(engine.State.IsGameOver);
        Assert.True(engine.State.IsDraw);
        Assert.Null(engine.State.WinnerIndex);
        Assert.Null(engine.State.PendingDrawRequester);
        Assert.Equal("双方同意因 Bug 平局", engine.State.GameOverReason);
    }

    [Fact]
    public void 连续被拒绝三次后不能再次申请()
    {
        var engine = CreateEngine();

        for (var attempt = 1; attempt <= GameState.DrawRequestRejectionLimit; attempt++)
        {
            Assert.True(engine.HandleAction(0, "RequestDraw", EmptyData));
            Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = false })));
            Assert.Equal(attempt, engine.State.DrawRequestRejectionCounts[0]);
            Assert.Null(engine.State.PendingDrawRequester);
            Assert.False(engine.State.IsGameOver);
        }

        Assert.False(engine.HandleAction(0, "RequestDraw", EmptyData));
        Assert.Null(engine.State.PendingDrawRequester);
        Assert.Equal(GameState.DrawRequestRejectionLimit, engine.State.DrawRequestRejectionCounts[0]);
    }

    [Fact]
    public void 只有对方可以回应且双方拒绝次数分别计算()
    {
        var engine = CreateEngine();

        Assert.True(engine.HandleAction(1, "RequestDraw", EmptyData));
        Assert.False(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = true })));
        Assert.Equal(1, engine.State.PendingDrawRequester);

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

        Assert.True(engine.HandleAction(0, "RequestDraw", EmptyData));

        Assert.True(player0.GetProperty("drawRequestPendingFromMe").GetBoolean());
        Assert.False(player0.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
        Assert.False(player1.GetProperty("drawRequestPendingFromMe").GetBoolean());
        Assert.True(player1.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
        Assert.Equal(0, player0.GetProperty("drawRequestRejectionCount").GetInt32());
        Assert.Equal(3, player0.GetProperty("drawRequestRejectionLimit").GetInt32());

        Assert.True(engine.HandleAction(1, "RespondDraw", JsonSerializer.SerializeToElement(new { accept = false })));
        Assert.Equal(1, player0.GetProperty("drawRequestRejectionCount").GetInt32());
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

            GameRoomManager.HandleAction(room.PlayerSessionIds[requester], "RequestDraw", EmptyData);
            await WaitUntilAsync(() => room.Engine.State.PendingDrawRequester == requester);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);

            var responder = 1 - requester;
            GameRoomManager.HandleAction(
                room.PlayerSessionIds[responder],
                "RespondDraw",
                JsonSerializer.SerializeToElement(new { accept = false }));
            await WaitUntilAsync(() => room.Engine.State.PendingDrawRequester is null);
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
