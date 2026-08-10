using System.Text.Json;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OperationClockTests
{
    [Fact]
    public async Task 选先后与调度不计时_进入第一回合后才启动棋钟()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            Assert.True(room.Engine.State.OperationClockEnabled);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);
            Assert.All(room.Engine.State.OperationClockRemainingMs, value => Assert.Equal(1_200_000, value));

            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            await Task.Delay(40);

            Assert.False(room.Engine.State.MulliganBothDone);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);
            Assert.All(room.Engine.State.OperationClockRemainingMs, value => Assert.Equal(1_200_000, value));

            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");

            Assert.True(room.Engine.State.MulliganBothDone);
            Assert.Equal(room.Engine.State.CurrentTurnPlayer, room.Engine.State.OperationClockActivePlayer);
            Assert.InRange(room.Engine.State.OperationClockRemainingMs[0], 1_199_000, 1_200_000);
            Assert.Equal(1_200_000, room.Engine.State.OperationClockRemainingMs[1]);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 单方总操作时间耗尽_直接判负()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            var active = room.Engine.State.CurrentTurnPlayer;
            room.Engine.State.OperationClockRemainingMs[active] = 30;
            room.Engine.Broadcast("ClockTest");

            await WaitUntilAsync(() => room.Engine.State.IsGameOver);

            Assert.Equal(1 - active, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.OperationClockRemainingMs[active]);
            Assert.Contains("操作时间耗尽", room.Engine.State.GameOverReason);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 断线宽限为每局累计两分钟_重连不会重置额度()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        var account = room.PlayerAccounts[0];
        try
        {
            var firstSession = room.PlayerSessionIds[0];
            GameRoomManager.OnPlayerDisconnect(firstSession);
            await Task.Delay(40);
            var secondSession = $"clock-reclaim-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(secondSession, account));
            var remainingAfterFirst = ReadDisconnectGrace(room, 0);

            Assert.InRange(remainingAfterFirst, 118_000, 119_999);

            GameRoomManager.OnPlayerDisconnect(secondSession);
            await Task.Delay(40);
            var thirdSession = $"clock-reclaim-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(thirdSession, account));
            var remainingAfterSecond = ReadDisconnectGrace(room, 0);

            Assert.True(remainingAfterSecond < remainingAfterFirst,
                $"第二次断线后剩余额度应继续减少：首次={remainingAfterFirst}，再次={remainingAfterSecond}");
        }
        finally
        {
            Cleanup(room);
        }
    }

    private static GameRoomManager.RoomEntry CreateRankedRoom()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return GameRoomManager.CreateRoom(
            $"clock-s0-{suffix}", $"clock-a-{suffix}", BuildLegalDeck("OP15-001"),
            $"clock-s1-{suffix}", $"clock-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true,
            matchKind: MatchKind.Ranked,
            broadcastInitialState: false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition(), "棋钟未在预期时间内完成超时结算");
    }

    private static long ReadDisconnectGrace(GameRoomManager.RoomEntry room, int playerIndex)
    {
        var property = typeof(GameRoomManager.RoomEntry).GetProperty(
            "DisconnectGraceRemainingMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((long[])property.GetValue(room)!)[playerIndex];
    }

    private static void Cleanup(GameRoomManager.RoomEntry room)
    {
        GameRoomManager.CleanupRoom(room.RoomId);
        TryDelete(room.ReplayPath);
        TryDelete(room.MatchLogPath);
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { }
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
