using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class OperationClockTests
{
    [Fact]
    public void 断线超时终局在效果批次挂起时也会立即下发快照()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            var sentPlayers = new List<int>();
            room.Engine.OnSendToPlayer = (playerIndex, _) => sentPlayers.Add(playerIndex);
            typeof(GameEngine).GetField("_snapshotBatchActive", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(room.Engine, true);

            room.Engine.State.WinnerIndex = 1;
            room.Engine.State.GameOverReason = "玩家断线超时";
            room.Engine.Broadcast("DisconnectTimeout", new { disconnected = 0 });

            Assert.Equal([0, 1], sentPlayers);
        }
        finally
        {
            Cleanup(room);
        }
    }

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
            Assert.All(room.Engine.State.OperationTurnClockRemainingMs, value => Assert.Equal(360_000, value));

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
    public async Task 单方本回合六分钟操作时间耗尽_直接判负()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            var active = room.Engine.State.CurrentTurnPlayer;
            room.Engine.State.OperationTurnClockRemainingMs[active] = 30;
            room.Engine.Broadcast("TurnClockTest");

            Assert.Equal(active, room.Engine.State.OperationClockActivePlayer);
            Assert.InRange(room.Engine.State.OperationTurnClockRemainingMs[active], 1, 30);

            await WaitUntilAsync(() => room.Engine.State.IsGameOver);

            Assert.Equal(1 - active, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.OperationTurnClockRemainingMs[active]);
            Assert.True(room.Engine.State.OperationClockRemainingMs[active] > 0);
            Assert.Contains("本回合操作时间耗尽", room.Engine.State.GameOverReason);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 超时任务提前唤醒_会按剩余回合时间重新挂载并判负()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            var active = room.Engine.State.CurrentTurnPlayer;

            // 先让服务端挂载一个很快到期的任务，再模拟该任务醒来前权威剩余时间被校正得更长。
            // 第一次回调不会判负，但必须重新按校正后的剩余时间挂载，而不能静默停钟。
            room.Engine.State.OperationTurnClockRemainingMs[active] = 30;
            room.Engine.Broadcast("TurnClockEarlyWakeTest");
            room.Engine.State.OperationTurnClockRemainingMs[active] = 500;

            await WaitUntilAsync(() => room.Engine.State.IsGameOver);

            Assert.Equal(1 - active, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.OperationTurnClockRemainingMs[active]);
            Assert.Contains("本回合操作时间耗尽", room.Engine.State.GameOverReason);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public void 新回合操作时间重置为六分钟与总剩余时间的较小值()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            var active = room.Engine.State.CurrentTurnPlayer;
            var next = 1 - active;
            room.Engine.State.OperationClockRemainingMs[next] = 300_000;
            room.Engine.State.OperationTurnClockRemainingMs[next] = 1_000;

            room.Engine.HandleAction(active, "EndTurn", JsonSerializer.SerializeToElement(new { }));
            room.Engine.Broadcast("NextTurnClockTest");

            Assert.Equal(next, room.Engine.State.CurrentTurnPlayer);
            Assert.InRange(room.Engine.State.OperationTurnClockRemainingMs[next], 299_000, 300_000);
            Assert.Equal(room.Engine.State.TurnCount, room.Engine.State.OperationTurnClockTurnCount);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 休闲对局同样启用双方二十分钟操作棋钟()
    {
        TestScene.New();
        var room = CreateTimedRoom(MatchKind.Casual);
        try
        {
            Assert.True(room.Engine.State.OperationClockEnabled);
            Assert.All(room.Engine.State.OperationClockRemainingMs, value => Assert.Equal(1_200_000, value));

            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");

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
    public void 狂野排位同样启用双方二十分钟操作棋钟()
    {
        TestScene.New();
        var room = CreateTimedRoom(MatchKind.RankedWild);
        try
        {
            Assert.True(room.Engine.State.OperationClockEnabled);
            Assert.All(room.Engine.State.OperationClockRemainingMs, value => Assert.Equal(1_200_000, value));
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 断线宽限为每局累计九十秒_重连不会重置额度()
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

            Assert.InRange(remainingAfterFirst, 88_000, 89_999);

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

    [Fact]
    public async Task 对局中断线重连后_操作棋钟立即继续运行()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            Assert.Equal(room.Engine.State.CurrentTurnPlayer, room.Engine.State.OperationClockActivePlayer);

            var oldSession = room.PlayerSessionIds[0];
            GameRoomManager.OnPlayerDisconnect(oldSession);
            Assert.True(room.Engine.State.OperationClockPaused);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);

            var newSession = $"clock-resume-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(newSession, room.PlayerAccounts[0]));
            await WaitUntilAsync(() => room.Engine.State.OperationClockActivePlayer >= 0);

            Assert.False(room.Engine.State.OperationClockPaused);
            Assert.Equal(room.Engine.State.CurrentTurnPlayer, room.Engine.State.OperationClockActivePlayer);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 在线账号被新会话接管_不会重复广播玩家重连()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            var tickBefore = room.Engine.State.Tick;
            var newSession = $"clock-takeover-{Guid.NewGuid():N}";

            Assert.True(GameRoomManager.TryReclaim(newSession, room.PlayerAccounts[0]));
            await Task.Delay(80);

            Assert.Equal(tickBefore, room.Engine.State.Tick);
            Assert.Same(room, GameRoomManager.GetRoomBySession(newSession));
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 每位玩家每局只能把当前回合加时一次至最多八分钟()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var active = room.Engine.State.CurrentTurnPlayer;
            var before = room.Engine.State.OperationTurnClockRemainingMs[active];

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "RequestTurnExtension",
                JsonSerializer.SerializeToElement(new { }),
                requestId: $"extend-{Guid.NewGuid():N}",
                receivedAt: Stopwatch.GetTimestamp());
            await WaitUntilAsync(() => room.Engine.State.OperationTurnExtensionUsed[active]);
            var afterFirst = room.Engine.State.OperationTurnClockRemainingMs[active];

            Assert.InRange(afterFirst, before + 118_000, before + 120_000);
            Assert.True(afterFirst <= GameRoomManager.OperationTurnExtendedTimeLimitMs);

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "RequestTurnExtension",
                JsonSerializer.SerializeToElement(new { }),
                requestId: $"extend-{Guid.NewGuid():N}",
                receivedAt: Stopwatch.GetTimestamp());
            await Task.Delay(80);

            Assert.True(room.Engine.State.OperationTurnExtensionUsed[active]);
            Assert.InRange(
                room.Engine.State.OperationTurnClockRemainingMs[active],
                afterFirst - 2_000,
                afterFirst);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 旧房间已有接近八分钟时申请加时仍不会突破上限()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var active = room.Engine.State.CurrentTurnPlayer;
            room.Engine.State.OperationTurnClockRemainingMs[active] = 470_000;

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "RequestTurnExtension",
                JsonSerializer.SerializeToElement(new { }),
                requestId: $"extend-cap-{Guid.NewGuid():N}",
                receivedAt: Stopwatch.GetTimestamp());
            await WaitUntilAsync(() => room.Engine.State.OperationTurnExtensionUsed[active]);

            Assert.InRange(
                room.Engine.State.OperationTurnClockRemainingMs[active],
                478_000,
                GameRoomManager.OperationTurnExtendedTimeLimitMs);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 玩家活动会把连续无操作计时归零并重新开始()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var active = room.Engine.State.CurrentTurnPlayer;
            var firstStartedAt = Stopwatch.GetTimestamp() - StopwatchTicks(61_000);
            room.InactivityActiveSince = firstStartedAt;

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "PlayerActivity",
                JsonSerializer.SerializeToElement(new { kind = "attachDon" }),
                receivedAt: Stopwatch.GetTimestamp());
            await WaitUntilAsync(() =>
                room.InactivityActiveSince > firstStartedAt
                && room.Engine.State.InactivityActivePlayer == active
                && room.Engine.State.InactivityLossRemainingMs > 238_000);

            Assert.Equal(active, room.Engine.State.InactivityActivePlayer);
            Assert.False(room.Engine.State.InactivityWarningActive);
            Assert.InRange(room.Engine.State.InactivityLossRemainingMs, 238_000, 240_000);

            // 第二段等待同样独立计算，不会与上一段跨操作累计。
            var secondStartedAt = Stopwatch.GetTimestamp() - StopwatchTicks(61_000);
            room.InactivityActiveSince = secondStartedAt;
            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "PlayerActivity",
                JsonSerializer.SerializeToElement(new { kind = "undoAttachDon" }),
                receivedAt: Stopwatch.GetTimestamp());
            await WaitUntilAsync(() =>
                room.InactivityActiveSince > secondStartedAt
                && room.Engine.State.InactivityActivePlayer == active
                && room.Engine.State.InactivityLossRemainingMs > 238_000);

            Assert.False(room.Engine.State.InactivityWarningActive);
            Assert.InRange(room.Engine.State.InactivityLossRemainingMs, 238_000, 240_000);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 连续四分钟没有操作后由服务端判负()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var active = room.Engine.State.CurrentTurnPlayer;
            room.InactivityActiveSince = Stopwatch.GetTimestamp() - StopwatchTicks(240_100);
            room.Engine.Broadcast("InactivityTimeoutTest");

            await WaitUntilAsync(() => room.Engine.State.IsGameOver);

            Assert.Equal(1 - active, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.InactivityLossRemainingMs);
            Assert.Contains("连续 4 分钟没有操作", room.Engine.State.GameOverReason);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task 对手断线暂停后重连不会清空当前玩家的连续等待段()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var active = room.Engine.State.CurrentTurnPlayer;
            var other = 1 - active;
            room.InactivityActiveSince = Stopwatch.GetTimestamp() - StopwatchTicks(70_000);

            var oldSession = room.PlayerSessionIds[other];
            GameRoomManager.OnPlayerDisconnect(oldSession);
            Assert.Equal(active, room.InactivityPausedPlayer);
            Assert.InRange(room.InactivityPausedElapsedMs, 69_900, 72_000);

            var newSession = $"clock-inactivity-resume-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(newSession, room.PlayerAccounts[other]));
            await WaitUntilAsync(() => room.Engine.State.OperationClockActivePlayer == active);

            Assert.Equal(active, room.Engine.State.InactivityActivePlayer);
            Assert.True(room.Engine.State.InactivityWarningActive);
            Assert.InRange(room.Engine.State.InactivityLossRemainingMs, 168_000, 171_000);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public void 私有诊断快照包含对局类型与完整棋钟状态()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");

            using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(
                PrivateStateSnapshotBuilder.Build(room.Engine.State)));
            var root = snapshot.RootElement;

            Assert.True(root.GetProperty("operationClockEnabled").GetBoolean());
            Assert.Equal(2, root.GetProperty("operationClockRemainingMs").GetArrayLength());
            Assert.Equal(2, root.GetProperty("operationTurnClockRemainingMs").GetArrayLength());
            Assert.Equal(room.Engine.State.TurnCount,
                root.GetProperty("operationTurnClockTurnCount").GetInt32());
            Assert.Equal(2, root.GetProperty("operationTurnExtensionUsed").GetArrayLength());
            Assert.False(root.TryGetProperty("inactivityPenaltyAccumulatedMs", out _));
            Assert.Equal(room.Engine.State.InactivityLossRemainingMs,
                root.GetProperty("inactivityLossRemainingMs").GetInt64());
            Assert.Equal(room.Engine.State.InactivityActivePlayer,
                root.GetProperty("inactivityActivePlayer").GetInt32());
            Assert.Equal(room.Engine.State.CurrentTurnPlayer,
                root.GetProperty("operationClockActivePlayer").GetInt32());
            Assert.False(root.GetProperty("operationClockPaused").GetBoolean());
            Assert.Equal("Ranked", root.GetProperty("matchKind").GetString());
        }
        finally
        {
            Cleanup(room);
        }
    }

    private static GameRoomManager.RoomEntry CreateRankedRoom()
        => CreateTimedRoom(MatchKind.Ranked);

    private static GameRoomManager.RoomEntry CreateTimedRoom(MatchKind matchKind)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return GameRoomManager.CreateRoom(
            $"clock-s0-{suffix}", $"clock-a-{suffix}", BuildLegalDeck("OP15-001"),
            $"clock-s1-{suffix}", $"clock-b-{suffix}", BuildLegalDeck("OP15-001"),
            p0First: true,
            matchKind: matchKind,
            broadcastInitialState: false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "棋钟未在预期时间内完成超时结算");
    }

    private static long ReadDisconnectGrace(GameRoomManager.RoomEntry room, int playerIndex)
    {
        var property = typeof(GameRoomManager.RoomEntry).GetProperty(
            "DisconnectGraceRemainingMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((long[])property.GetValue(room)!)[playerIndex];
    }

    private static long StopwatchTicks(long milliseconds)
        => (long)Math.Ceiling(milliseconds * (double)Stopwatch.Frequency / 1000d);

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
