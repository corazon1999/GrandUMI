using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class StartingPlayerChoiceRecoveryTests
{
    [Fact]
    public async Task 房间先后手计时_截止后骰点胜者自动选择先手()
    {
        TestScene.New();
        var room = CreateRoom("starting-choice-timeout");

        try
        {
            var chooser = room.Engine.State.StartingPlayerChooser;
            room.Engine.State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddMilliseconds(50);

            GameRoomManager.HandleRequestState(room.PlayerSessionIds[0]);
            await WaitUntilAsync(() => room.Engine.State.StartingPlayerChosen);

            Assert.Equal(chooser, room.Engine.State.FirstPlayer);
            Assert.Null(room.Engine.State.StartingPlayerChoiceDeadlineUtc);
            Assert.NotNull(room.Engine.State.MulliganDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 玩家及时选择后_迟到的超时任务不会覆盖选择结果()
    {
        TestScene.New();
        var room = CreateRoom("starting-choice-manual");

        try
        {
            var chooser = room.Engine.State.StartingPlayerChooser;
            var expectedFirstPlayer = 1 - chooser;
            room.Engine.State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddMilliseconds(100);
            GameRoomManager.HandleRequestState(room.PlayerSessionIds[chooser]);

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[chooser],
                "ChooseFirstPlayer",
                JsonSerializer.SerializeToElement(new { goFirst = false }));
            await WaitUntilAsync(() => room.Engine.State.StartingPlayerChosen);
            await Task.Delay(150);

            Assert.Equal(expectedFirstPlayer, room.Engine.State.FirstPlayer);
            Assert.Null(room.Engine.State.StartingPlayerChoiceDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 刷新取状态_会补做已经过期的先后手选择()
    {
        TestScene.New();
        var room = CreateRoom("starting-choice-resync");

        try
        {
            var chooser = room.Engine.State.StartingPlayerChooser;
            room.Engine.State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddSeconds(-1);

            GameRoomManager.HandleRequestState(room.PlayerSessionIds[1 - chooser]);
            await WaitUntilAsync(() => room.Engine.State.StartingPlayerChosen);

            Assert.Equal(chooser, room.Engine.State.FirstPlayer);
            Assert.Null(room.Engine.State.StartingPlayerChoiceDeadlineUtc);
        }
        finally
        {
            Cleanup(room);
        }
    }

    private static GameRoomManager.RoomEntry CreateRoom(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return GameRoomManager.CreateRoom(
            $"{prefix}-s0-{suffix}", $"{prefix}-a-{suffix}", BuildLegalDeck("OP15-001"),
            $"{prefix}-s1-{suffix}", $"{prefix}-b-{suffix}", BuildLegalDeck("OP15-001"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(4);
        Assert.True(condition(), "房间队列未在预期时间内完成先后手选择恢复");
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
