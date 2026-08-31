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
    public void 挂机超时终局在效果批次挂起时也会立即下发并停止全部计时()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            var sentPlayers = new List<int>();
            room.Engine.OnSendToPlayer = (playerIndex, _) => sentPlayers.Add(playerIndex);
            typeof(GameEngine).GetField("_snapshotBatchActive", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(room.Engine, true);
            room.Engine.State.PendingPrompt = new PendingPrompt
            {
                PromptId = "stalled-prompt",
                PlayerIndex = 0,
                Kind = "Confirm",
                ValidChoices = ["yes", "no"],
                MinChoose = 1,
                MaxChoose = 1,
                PromptText = "等待玩家选择",
            };

            typeof(GameRoomManager).GetMethod(
                    "FinishByInactivityTimeout",
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [room, 0]);

            Assert.True(room.Engine.State.IsGameOver);
            Assert.Equal(1, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.InactivityLossRemainingMs);
            Assert.Equal(-1, room.Engine.State.OperationClockActivePlayer);
            Assert.Equal([0, 1], sentPlayers);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public void 新增终局原因即使未登记为交互屏障也必须立即下发()
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
            room.Engine.State.GameOverReason = "未来新增的终局原因";
            room.Engine.Broadcast("FutureTerminalAction");

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
    public async Task Q488_真实贴咚会重置无操作计时且重复RequestId不会重复效果()
    {
        TestScene.New();
        var room = CreateTimedRoom(MatchKind.Ranked, "OP02-002");
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var state = room.Engine.State;
            var active = state.CurrentTurnPlayer;
            var me = state.Players[active];
            var opponent = state.Players[1 - active];
            state.Phase = Phase.Main;
            state.TurnCount = 3;
            state.OperationTurnClockTurnCount = state.TurnCount;
            me.CostArea.Clear();
            me.CostArea.AddRange([
                new DonCard { State = DonState.Active },
                new DonCard { State = DonState.Active },
            ]);
            opponent.Characters.Clear();
            var costTarget = new CardInstance { Info = CardDatabase.Get("OP15-003")! };
            opponent.Characters.Add(costTarget);

            var inactivityBefore = Stopwatch.GetTimestamp() - StopwatchTicks(61_000);
            room.InactivityActivePlayer = active;
            room.InactivityActiveSince = inactivityBefore;
            var totalBefore = state.OperationClockRemainingMs[active];
            var turnBefore = state.OperationTurnClockRemainingMs[active];
            var originalSendToPlayer = room.Engine.OnSendToPlayer;
            var delayedPromptSnapshot = 0;
            room.Engine.OnSendToPlayer = (playerIndex, payload) =>
            {
                var snapshot = JsonSerializer.SerializeToElement(payload);
                if (playerIndex == active
                    && snapshot.TryGetProperty("lastAction", out var lastAction)
                    && lastAction.GetString() == "Prompt"
                    && Interlocked.Exchange(ref delayedPromptSnapshot, 1) == 0)
                {
                    // BeforeSnapshot 已经运行；模拟随后仍有较慢的服务端效果下发工作。
                    Thread.Sleep(400);
                }
                originalSendToPlayer?.Invoke(playerIndex, payload);
            };
            const string attachRequestId = "q488-attach-don-once";

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "AttachDon",
                JsonSerializer.SerializeToElement(new { targetId = "leader", count = 2 }),
                requestId: attachRequestId,
                receivedAt: Stopwatch.GetTimestamp());

            await WaitUntilAsync(() =>
                me.AttachedDonCount(me.Leader.Id) == 2
                && state.PendingPrompt is not null);
            await WaitUntilAsync(() =>
                room.InactivityActiveSince > inactivityBefore
                && state.InactivityLossRemainingMs > 238_000
                && state.OperationClockActivePlayer == active
                && state.InactivityActivePlayer == active);
            var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
            Assert.Equal(1, Volatile.Read(ref delayedPromptSnapshot));
            Assert.InRange(state.OperationClockRemainingMs[active], totalBefore - 200, totalBefore);
            Assert.InRange(state.OperationTurnClockRemainingMs[active], turnBefore - 200, turnBefore);

            // 首个动作尚在效果选择窗口内就重发同一 requestId，仍只能回权威快照，不能再赋咚或再派发效果。
            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "AttachDon",
                JsonSerializer.SerializeToElement(new { targetId = "leader", count = 2 }),
                requestId: attachRequestId,
                receivedAt: Stopwatch.GetTimestamp());
            await Task.Delay(100);

            Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
            Assert.Same(prompt, state.PendingPrompt);
            Assert.Equal(0, costTarget.CostModThisTurn);
            Assert.False(state.InactivityWarningActive);
            Assert.InRange(state.InactivityLossRemainingMs, 238_000, 240_000);

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "PromptResponse",
                JsonSerializer.SerializeToElement(new
                {
                    promptId = prompt.PromptId,
                    chosen = new[] { costTarget.Id.ToString() },
                }),
                requestId: "q488-garp-target",
                receivedAt: Stopwatch.GetTimestamp());
            await WaitUntilAsync(() => state.PendingPrompt is null && costTarget.CostModThisTurn == -1);

            Assert.Equal(-1, costTarget.CostModThisTurn);
            Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
            Assert.InRange(state.OperationClockRemainingMs[active], totalBefore - 2_000, totalBefore);
            Assert.InRange(state.OperationTurnClockRemainingMs[active], turnBefore - 2_000, turnBefore);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task Q488_无Prompt贴咚后棋钟与无操作计时保持同一权威玩家()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var state = room.Engine.State;
            var active = state.CurrentTurnPlayer;
            var me = state.Players[active];
            state.Phase = Phase.Main;
            state.TurnCount = 3;
            state.OperationTurnClockTurnCount = state.TurnCount;
            me.CostArea.Clear();
            me.CostArea.Add(new DonCard { State = DonState.Active });
            var inactivityBefore = Stopwatch.GetTimestamp() - StopwatchTicks(61_000);
            room.InactivityActivePlayer = active;
            room.InactivityActiveSince = inactivityBefore;

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "AttachDon",
                JsonSerializer.SerializeToElement(new { targetId = "leader", count = 1 }),
                requestId: "q488-no-prompt-attach-don",
                receivedAt: Stopwatch.GetTimestamp());

            await WaitUntilAsync(() =>
                me.AttachedDonCount(me.Leader.Id) == 1
                && state.PendingPrompt is null
                && room.InactivityActiveSince > inactivityBefore
                && state.InactivityLossRemainingMs > 238_000
                && state.OperationClockActivePlayer == active
                && state.InactivityActivePlayer == active);

            Assert.False(state.InactivityWarningActive);
            Assert.InRange(state.InactivityLossRemainingMs, 238_000, 240_000);
        }
        finally
        {
            Cleanup(room);
        }
    }

    [Fact]
    public async Task Q488_非法贴咚不会重置无操作计时或改变状态()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.Broadcast("ClockTest");
            var state = room.Engine.State;
            var active = state.CurrentTurnPlayer;
            var me = state.Players[active];
            state.Phase = Phase.Main;
            state.TurnCount = 3;
            me.CostArea.Clear();
            me.CostArea.AddRange([
                new DonCard { State = DonState.Active },
                new DonCard { State = DonState.Active },
            ]);
            var inactivityBefore = Stopwatch.GetTimestamp() - StopwatchTicks(61_000);
            room.InactivityActivePlayer = active;
            room.InactivityActiveSince = inactivityBefore;
            var rejected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            room.Engine.OnSendToPlayer = (playerIndex, payload) =>
            {
                var message = JsonSerializer.SerializeToElement(payload);
                if (playerIndex == active
                    && message.TryGetProperty("proto", out var proto)
                    && proto.GetString() == "MsgActionRejected")
                    rejected.TrySetResult(true);
            };

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[active],
                "AttachDon",
                JsonSerializer.SerializeToElement(new { targetId = "leader", count = 0 }),
                requestId: "q488-invalid-attach-don",
                receivedAt: Stopwatch.GetTimestamp());

            await rejected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitUntilAsync(() =>
                room.InactivityActivePlayer == active
                && room.InactivityActiveSince > 0
                && state.InactivityWarningActive);

            Assert.Equal(2, me.ActiveDonCount);
            Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
            Assert.InRange(state.InactivityLossRemainingMs, 178_000, 180_000);
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

    [Fact]
    public async Task G620_攻击效果等待选择时只扣提示决策方的操作时间()
    {
        TestScene.New();
        var room = CreateRankedRoom();
        try
        {
            room.Engine.HandleAction(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            room.Engine.HandleAction(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
            var state = room.Engine.State;
            var attacker = state.CurrentTurnPlayer;
            var defender = 1 - attacker;
            state.Phase = Phase.BattleCounter;
            state.CurrentBattle = new BattleContext
            {
                AttackerPlayerIndex = attacker,
                DefenderPlayerIndex = defender,
                AttackerCardId = state.Players[attacker].Leader.Id,
                TargetIsLeader = true,
            };
            room.Engine.Broadcast("G620BeforePrompt");
            await WaitUntilAsync(() => state.OperationClockActivePlayer == defender);

            var choiceTask = room.Engine.Prompts.ChooseCards(
                attacker,
                "AttackEffect",
                "选择攻击时效果对象",
                ["attack-choice"],
                1,
                1);
            await WaitUntilAsync(() =>
                state.PendingPrompt is { PlayerIndex: var player } && player == attacker
                && state.OperationClockActivePlayer == attacker);

            var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
            var attackerBefore = state.OperationClockRemainingMs[attacker];
            var defenderBefore = state.OperationClockRemainingMs[defender];
            room.OperationClockActiveSince = Stopwatch.GetTimestamp() - StopwatchTicks(1_000);

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[attacker],
                "PromptResponse",
                JsonSerializer.SerializeToElement(new
                {
                    promptId = prompt.PromptId,
                    chosen = new[] { "attack-choice" },
                }),
                requestId: "g620-attack-choice",
                receivedAt: Stopwatch.GetTimestamp());

            Assert.Equal(["attack-choice"], await choiceTask.WaitAsync(TimeSpan.FromSeconds(3)));
            await WaitUntilAsync(() =>
                state.PendingPrompt is null && state.OperationClockActivePlayer == defender);

            Assert.InRange(
                attackerBefore - state.OperationClockRemainingMs[attacker],
                900,
                1_200);
            Assert.InRange(
                defenderBefore - state.OperationClockRemainingMs[defender],
                0,
                200);
        }
        finally
        {
            Cleanup(room);
        }
    }

    private static GameRoomManager.RoomEntry CreateRankedRoom()
        => CreateTimedRoom(MatchKind.Ranked);

    private static GameRoomManager.RoomEntry CreateTimedRoom(
        MatchKind matchKind,
        string leaderNumber = "OP15-001")
    {
        var suffix = Guid.NewGuid().ToString("N");
        return GameRoomManager.CreateRoom(
            $"clock-s0-{suffix}", $"clock-a-{suffix}", BuildLegalDeck(leaderNumber),
            $"clock-s1-{suffix}", $"clock-b-{suffix}", BuildLegalDeck(leaderNumber),
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
