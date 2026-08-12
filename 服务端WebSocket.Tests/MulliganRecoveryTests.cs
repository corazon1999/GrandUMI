using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class MulliganRecoveryTests
{
    [Fact]
    public async Task 房间调度计时_截止后可靠自动保留并进入第一回合()
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var player0Session = $"mulligan-timer-s0-{suffix}";
        var player1Session = $"mulligan-timer-s1-{suffix}";
        var room = GameRoomManager.CreateRoom(
            player0Session, $"mulligan-timer-a-{suffix}", BuildLegalDeck("OP15-001"),
            player1Session, $"mulligan-timer-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true);

        try
        {
            room.Engine.State.MulliganDeadlineUtc = DateTime.UtcNow.AddMilliseconds(50);

            // 取状态会重新核对权威截止时间并确保对应计时器存在。
            GameRoomManager.HandleRequestState(player0Session);
            await WaitUntilAsync(() => room.Engine.State.MulliganBothDone);

            Assert.All(room.Engine.State.Players, player => Assert.True(player.MulliganDone));
            Assert.Equal(1, room.Engine.State.TurnCount);
            Assert.Null(room.Engine.State.MulliganDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 刷新重绑_会先补做已过期调度再恢复对局()
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var player0Session = $"mulligan-reclaim-s0-{suffix}";
        var player1Session = $"mulligan-reclaim-s1-{suffix}";
        var player0Account = $"mulligan-reclaim-a-{suffix}";
        var room = GameRoomManager.CreateRoom(
            player0Session, player0Account, BuildLegalDeck("OP15-001"),
            player1Session, $"mulligan-reclaim-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true);

        try
        {
            room.Engine.State.MulliganDeadlineUtc = DateTime.UtcNow.AddSeconds(-1);
            var newSession = $"mulligan-reclaim-new-{suffix}";

            Assert.True(GameRoomManager.TryReclaim(newSession, player0Account));
            await WaitUntilAsync(() => room.Engine.State.MulliganBothDone);

            Assert.Same(room, GameRoomManager.GetRoomBySession(newSession));
            Assert.Equal(1, room.Engine.State.TurnCount);
            Assert.Null(room.Engine.State.MulliganDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 单方已完成调度_取状态会为超时对手自动保留并进入第一回合()
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var player0Session = $"mulligan-one-sided-s0-{suffix}";
        var player1Session = $"mulligan-one-sided-s1-{suffix}";
        var room = GameRoomManager.CreateRoom(
            player0Session, $"mulligan-one-sided-a-{suffix}", BuildLegalDeck("OP15-001"),
            player1Session, $"mulligan-one-sided-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true);

        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = true }));
            Assert.True(room.Engine.State.Players[0].MulliganDone);
            Assert.False(room.Engine.State.Players[1].MulliganDone);
            Assert.False(room.Engine.State.Players[0].HasReDraw);

            room.Engine.State.MulliganDeadlineUtc = DateTime.UtcNow.AddSeconds(-1);
            GameRoomManager.HandleRequestState(player0Session);
            await WaitUntilAsync(() => room.Engine.State.MulliganBothDone);

            Assert.True(room.Engine.State.Players[1].MulliganDone);
            Assert.True(room.Engine.State.Players[1].HasReDraw);
            Assert.False(room.Engine.State.Players[0].HasReDraw);
            Assert.Equal(1, room.Engine.State.TurnCount);
            Assert.Null(room.Engine.State.MulliganDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(4);
        Assert.True(condition(), "房间队列未在预期时间内完成调度恢复");
    }

    private static void Cleanup(GameRoomManager.RoomEntry room)
    {
        GameRoomManager.CleanupRoom(room.RoomId);
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
