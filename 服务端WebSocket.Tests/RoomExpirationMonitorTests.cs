using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class RoomExpirationMonitorTests
{
    [Fact]
    public void 未超过无操作期限的房间不会被清理()
    {
        TestScene.New();
        var room = CreateRoom("active");
        try
        {
            var beforeDeadline = room.LastActivityUtc
                .Add(GameRoomManager.RoomInactivityTimeout)
                .AddTicks(-1);

            Assert.False(GameRoomManager.TryCleanupExpiredRoom(room.RoomId, beforeDeadline));
            Assert.Same(room, GameRoomManager.GetRoom(room.RoomId));
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
        }
    }

    [Fact]
    public void 连续三十分钟无有效操作的房间会被清理()
    {
        TestScene.New();
        var room = CreateRoom("inactive");
        try
        {
            var afterDeadline = room.LastActivityUtc
                .Add(GameRoomManager.RoomInactivityTimeout)
                .AddTicks(1);

            Assert.True(GameRoomManager.TryCleanupExpiredRoom(room.RoomId, afterDeadline));
            Assert.Null(GameRoomManager.GetRoom(room.RoomId));
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
        }
    }

    [Fact]
    public void 已终局但残留的房间无需等待超时即可清理()
    {
        TestScene.New();
        var room = CreateRoom("terminal");
        try
        {
            room.Engine.State.WinnerIndex = 0;

            Assert.True(GameRoomManager.TryCleanupExpiredRoom(room.RoomId, room.LastActivityUtc));
            Assert.Null(GameRoomManager.GetRoom(room.RoomId));
        }
        finally
        {
            GameRoomManager.CleanupRoom(room.RoomId);
        }
    }

    private static GameRoomManager.RoomEntry CreateRoom(string prefix)
        => GameRoomManager.CreateRoom(
            $"{prefix}-s0-{Guid.NewGuid():N}",
            $"{prefix}-a0-{Guid.NewGuid():N}",
            BuildLegalDeck("OP15-001"),
            $"{prefix}-s1-{Guid.NewGuid():N}",
            $"{prefix}-a1-{Guid.NewGuid():N}",
            BuildLegalDeck("OP15-001"),
            p0First: true,
            matchKind: MatchKind.Bot,
            broadcastInitialState: false);

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
