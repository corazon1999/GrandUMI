using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

[Collection("持久化目录隔离")]
public sealed class OpeningPreparationTests
{
    [Fact]
    public void 重复确认不延长等待且只完成一次()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var gate = new OpeningPreparationGate(3, deadline);
        Assert.False(gate.TryReady(0));
        Assert.False(gate.TryReady(0));
        Assert.Equal(deadline, gate.DeadlineUtc);
        Assert.False(gate.TryExpire(deadline.AddTicks(-1)));
        Assert.True(gate.TryReady(1));
        Assert.False(gate.TryReady(1));
        Assert.False(gate.TryExpire(deadline));
        gate.Cancel();
    }

    [Fact]
    public async Task 准备快照不发牌且双方确认后才开始完整调度()
    {
        var room = CreateRoom(3);
        try
        {
            Assert.Null(room.Engine.State.MulliganDeadlineUtc);
            Assert.All(room.Engine.State.Players, player => Assert.Empty(player.Hand));
            Assert.Equal(OpeningStage.NotStarted, room.Engine.State.OpeningStage);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
            await Task.Delay(30);
            Assert.Null(room.Engine.State.MulliganDeadlineUtc);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[1], room.RoomId);
            await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
            Assert.Null(room.Engine.State.OpeningPreparationDeadlineUtc);
            Assert.All(room.Engine.State.Players, player => Assert.Equal(5, player.Hand.Count));
            Assert.InRange((room.Engine.State.MulliganDeadlineUtc!.Value - DateTime.UtcNow).TotalSeconds, 58, 60);
            var deadline = room.Engine.State.MulliganDeadlineUtc;
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
            await Task.Delay(30);
            Assert.Equal(deadline, room.Engine.State.MulliganDeadlineUtc);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
    }

    [Fact]
    public async Task 缺少确认在固定期限后仍会开始完整调度()
    {
        var room = CreateRoom(3, broadcast: false);
        try
        {
            room.OpeningPreparation!.Cancel();
            room.OpeningPreparation = new OpeningPreparationGate(3, DateTime.UtcNow.AddMilliseconds(40));
            room.Engine.State.OpeningPreparationDeadlineUtc = room.OpeningPreparation.DeadlineUtc;
            GameRoomManager.BroadcastInitialState(room);
            await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
            Assert.Null(room.Engine.State.OpeningPreparationDeadlineUtc);
            Assert.InRange((room.Engine.State.MulliganDeadlineUtc!.Value - DateTime.UtcNow).TotalSeconds, 58, 60);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
    }

    [Fact]
    public async Task 过期房间标识或旁观会话不能确认且重绑会话可以确认()
    {
        var room = CreateRoom(1);
        try
        {
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], "过期房间");
            GameRoomManager.HandleClientPrepared("旁观会话", room.RoomId);
            await Task.Delay(30);
            Assert.NotNull(room.Engine.State.OpeningPreparationDeadlineUtc);
            var replacement = "prepared-rebound-" + Guid.NewGuid().ToString("N");
            Assert.True(GameRoomManager.TryReclaim(replacement, room.PlayerAccounts[0]));
            GameRoomManager.HandleClientPrepared(replacement, room.RoomId);
            await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
    }

    [Fact]
    public async Task 混合客户端仅等待支持方且准备中取状态不会提前开局()
    {
        var room = CreateRoom(2);
        try
        {
            GameRoomManager.HandleRequestState(room.PlayerSessionIds[0]);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
            await Task.Delay(30);
            Assert.Equal(OpeningStage.NotStarted, room.Engine.State.OpeningStage);
            var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(room.Engine.State, 0));
            Assert.Equal(room.RoomId, snapshot.GetProperty("roomId").GetString());
            Assert.Equal(JsonValueKind.String, snapshot.GetProperty("openingPreparationDeadlineUtc").ValueKind);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[1], room.RoomId);
            await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
            await WaitUntil(() => RoomRecoverySnapshotStore.TryRead(room.RoomId) is not null);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
    }

    [Fact]
    public async Task 单人模式只等待真人且清房取消准备任务()
    {
        var room = CreateRoom(3, vsBot: true);
        try
        {
            Assert.All(room.Engine.State.Players, player => Assert.Empty(player.Hand));
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
            await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
        var pending = CreateRoom(3);
        var token = pending.OpeningPreparation!.CancellationToken;
        GameRoomManager.CleanupRoom(pending.RoomId);
        Assert.Null(GameRoomManager.GetRoom(pending.RoomId));
        Assert.True(token.IsCancellationRequested);
        await Task.Delay(30);
        Assert.All(pending.Engine.State.Players, player => Assert.Empty(player.Hand));
    }

    [Fact]
    public void 旧客户端保持立即开局且准备中不保存未开局恢复检查点()
    {
        var legacy = CreateRoom(0);
        var pending = CreateRoom(3);
        try
        {
            Assert.Null(legacy.Engine.State.OpeningPreparationDeadlineUtc);
            Assert.NotNull(legacy.Engine.State.MulliganDeadlineUtc);
            GameRoomManager.CaptureAllRecoverySnapshots();
            Assert.Null(RoomRecoverySnapshotStore.TryRead(pending.RoomId));
        }
        finally
        {
            GameRoomManager.CleanupRoom(legacy.RoomId);
            GameRoomManager.CleanupRoom(pending.RoomId);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task 准备完成后伊姆开局选择可重同步并通过房间队列推进投骰(int owner, bool skip)
    {
        var room = CreateRoom(3, imuOwner: owner);
        try
        {
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[1], room.RoomId);
            await WaitUntil(() => room.Engine.State.PendingPrompt is not null);
            var prompt = room.Engine.State.PendingPrompt!;
            Assert.Equal(owner, prompt.PlayerIndex);
            Assert.Null(room.Engine.State.OpeningPreparationDeadlineUtc);
            Assert.Empty(room.Engine.State.StartingDiceRounds);
            Assert.All(room.Engine.State.Players, player => Assert.Empty(player.Hand));

            var mine = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(room.Engine.State, owner));
            var other = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(room.Engine.State, 1 - owner));
            Assert.Equal("WaitingOpeningPrompt", other.GetProperty("openingStage").GetString());
            Assert.Equal(JsonValueKind.Null, other.GetProperty("pendingPrompt").ValueKind);
            Assert.Equal(prompt.PromptId, mine.GetProperty("pendingPrompt").GetProperty("promptId").GetString());

            // 双方重同步及重复准备确认均不得重新开局或绕过尚未完成的选择。
            GameRoomManager.HandleRequestState(room.PlayerSessionIds[owner]);
            GameRoomManager.HandleRequestState(room.PlayerSessionIds[1 - owner]);
            GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[owner], room.RoomId);
            GameRoomManager.HandleAction(room.PlayerSessionIds[owner], "PromptResponse",
                JsonSerializer.SerializeToElement(new
                {
                    promptId = prompt.PromptId,
                    chosen = skip ? Array.Empty<string>() : new[] { prompt.ValidChoices[0] },
                }));
            await WaitUntil(() => room.Engine.State.OpeningStage == OpeningStage.WaitingFirstPlayerChoice);
            Assert.Null(room.Engine.State.PendingPrompt);
            Assert.NotEmpty(room.Engine.State.StartingDiceRounds);
            Assert.NotNull(room.Engine.State.StartingPlayerChoiceDeadlineUtc);
            Assert.Equal(skip ? null : "OP13-099", room.Engine.State.Players[owner].StageCard?.Info.Number);
            Assert.False(room.IsRecoveryPaused);
        }
        finally { GameRoomManager.CleanupRoom(room.RoomId); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 准备中或刚完成准备的建房日志可正常重启恢复(bool prepared)
    {
        var temp = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("恢复验证需要项目提供的临时目录");
        var root = Path.Combine(temp, "opening-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var restoredId = "prepared-restore-" + Guid.NewGuid().ToString("N");
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom(3);
            if (prepared)
            {
                GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[0], room.RoomId);
                GameRoomManager.HandleClientPrepared(room.PlayerSessionIds[1], room.RoomId);
                await WaitUntil(() => room.Engine.State.MulliganDeadlineUtc is not null);
            }
            var committed = await RoomJournal.ReadCommittedLinesAsync(Path.Combine(root, room.RoomId + ".jsonl"));
            var header = System.Text.Json.Nodes.JsonNode.Parse(committed.Lines[0])!;
            header["roomId"] = restoredId;
            GameRoomManager.CleanupRoom(room.RoomId);
            await RoomJournal.DeleteDeferred(room.RoomId);
            room = null;
            await File.WriteAllLinesAsync(Path.Combine(root, restoredId + ".jsonl"), [header.ToJsonString()]);
            await GameRoomManager.RestoreAll();
            var restored = Assert.IsType<GameRoomManager.RoomEntry>(GameRoomManager.GetRoom(restoredId));
            Assert.Null(restored.Engine.State.OpeningPreparationDeadlineUtc);
            Assert.All(restored.Engine.State.Players, player => Assert.Equal(5, player.Hand.Count));
            Assert.False(restored.IsRecoveryPaused);
        }
        finally
        {
            if (room is not null) GameRoomManager.CleanupRoom(room.RoomId);
            GameRoomManager.CleanupRoom(restoredId);
            await RoomJournal.DeleteDeferred(restoredId);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previous);
        }
    }

    private static GameRoomManager.RoomEntry CreateRoom(int mask, bool broadcast = true, bool vsBot = false,
        int? imuOwner = null)
    {
        TestScene.New();
        var suffix = Guid.NewGuid().ToString("N");
        var leader = CardDatabase.Get("OP15-001")!;
        var cards = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader)).Take(13).ToArray();
        var deck = string.Join('\n', new[] { leader.Number }.Concat(
            Enumerable.Range(0, 50).Select(index => cards[index / 4].Number)));
        var imu = CardDatabase.Get("OP13-079")!;
        var imuCards = CardDatabase.GetBySet("OP13")
            .Where(card => card.Kind != CardKind.Leader && card.Number != "OP13-099"
                && card.Kind != CardKind.Event && card.SharesColorWith(imu)).Take(12).ToArray();
        var imuDeck = string.Join('\n', new[] { imu.Number }.Concat(Enumerable.Repeat("OP13-099", 4))
            .Concat(Enumerable.Range(0, 46).Select(index => imuCards[index / 4].Number)));
        return GameRoomManager.CreateRoom(
            $"prepared-s0-{suffix}", $"prepared-a0-{suffix}", imuOwner == 0 ? imuDeck : deck,
            $"prepared-s1-{suffix}", $"prepared-a1-{suffix}", imuOwner == 1 ? imuDeck : deck,
            p0First: imuOwner is null ? true : null, openingReadinessMask: mask,
            broadcastInitialState: broadcast, vsBot: vsBot);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.True(predicate(), "开局准备未在预期时间内结束");
    }
}
