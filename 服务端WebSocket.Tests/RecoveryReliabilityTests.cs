using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMIServer.Tests;

[Collection("持久化目录隔离")]
public sealed class RecoveryReliabilityTests
{
    [Fact]
    public async Task 恢复日志写盘失败_已变更状态不下发且房间进入安全暂停()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("journal-failure");
        Directory.CreateDirectory(root);
        var previousRoot = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom();
            RoomJournal.DurableFailureInjector = (roomId, operation) =>
                roomId == room.RoomId && operation == "action"
                    ? new IOException("故障演练：磁盘不可写")
                    : null;

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[0],
                "Mulligan",
                JsonSerializer.SerializeToElement(new { redraw = false }),
                requestId: "durable-action-1");

            await WaitUntilAsync(() => room.IsRecoveryPaused);
            Assert.Equal("recovery_commit_failed", room.RecoveryPauseReason);
            Assert.True(room.Engine.State.Players[0].MulliganDone);

            GameRoomManager.OnPlayerDisconnect(room.PlayerSessionIds[0]);
            Assert.False(room.DisconnectedPlayers[0]);
            var reboundSession = $"recovery-rebound-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(reboundSession, room.PlayerAccounts[0]));
            GameRoomManager.HandleRequestState(reboundSession);

            var committed = await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId));
            Assert.Single(committed.Lines);
            Assert.Equal("create", JsonDocument.Parse(committed.Lines[0]).RootElement.GetProperty("kind").GetString());

            GameRoomManager.HandleAction(
                reboundSession,
                "Mulligan",
                JsonSerializer.SerializeToElement(new { redraw = true }),
                requestId: "durable-action-retry");
            await Task.Delay(50);
            Assert.Single((await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId))).Lines);
        }
        finally
        {
            RoomJournal.DurableFailureInjector = null;
            if (room is not null) GameRoomManager.CleanupRoom(room.RoomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousRoot);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 恢复快照写盘失败_动作日志已提交但客户端房间仍安全暂停()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("snapshot-failure");
        Directory.CreateDirectory(root);
        var previousRoot = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom();
            room.AcceptedActionsSinceSnapshot = RoomRecoverySnapshotStore.CaptureEveryAcceptedActions - 1;
            RoomRecoverySnapshotStore.WriteFailureInjector = roomId =>
                roomId == room.RoomId ? new IOException("故障演练：快照磁盘不可写") : null;

            GameRoomManager.HandleAction(
                room.PlayerSessionIds[0],
                "Mulligan",
                JsonSerializer.SerializeToElement(new { redraw = false }),
                requestId: "snapshot-action-1");

            await WaitUntilAsync(() => room.IsRecoveryPaused);
            var lines = (await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId))).Lines;
            Assert.Contains(lines, line =>
                JsonDocument.Parse(line).RootElement.TryGetProperty("requestId", out var request)
                && request.GetString() == "snapshot-action-1");
            Assert.True(RoomRecoverySnapshotStore.WriteFailures > 0);
        }
        finally
        {
            RoomRecoverySnapshotStore.WriteFailureInjector = null;
            if (room is not null) GameRoomManager.CleanupRoom(room.RoomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousRoot);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 进程在记录中途终止_只截断未确认尾行并恢复此前提交()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("kill-point");
        Directory.CreateDirectory(root);
        var previousRoot = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"kill-{Guid.NewGuid():N}"[..20];
        try
        {
            var path = Path.Combine(root, $"{roomId}.jsonl");
            var header = BuildHeader(roomId);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(header) + "\n{\"kind\":\"action\",\"journalSequence\":1");

            await GameRoomManager.RestoreAll();

            Assert.NotNull(GameRoomManager.GetRoom(roomId));
            var committed = await RoomJournal.ReadCommittedLinesAsync(path);
            Assert.False(committed.HadIncompleteTail);
            Assert.Single(committed.Lines);
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousRoot);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 同序号快照哈希分歧_恢复房间必须隔离而不是继续接受操作()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("hash-divergence");
        Directory.CreateDirectory(root);
        var previousRoot = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"diverge-{Guid.NewGuid():N}"[..24];
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, $"{roomId}.jsonl"),
                JsonSerializer.Serialize(BuildHeader(roomId)) + "\n");
            var fakeState = JsonSerializer.SerializeToElement(new { divergent = true });
            RoomRecoverySnapshotStore.Capture(new RoomRecoverySnapshot(
                RoomRecoverySnapshotStore.SchemaVersion,
                roomId,
                0,
                DateTime.UtcNow,
                [-1, -1],
                [1_200_000, 1_200_000],
                Array.Empty<RequestDedupeEntry>(),
                RoomRecoverySnapshotStore.ComputeStateSha256(fakeState),
                fakeState));

            await GameRoomManager.RestoreAll();

            Assert.Null(GameRoomManager.GetRoom(roomId));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "quarantine"), $"{roomId}-*.jsonl"));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "quarantine"), $"{roomId}-*.snapshot.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousRoot);
            TryDelete(root);
        }
    }

    [Fact]
    public void 恢复确定性投影_忽略广播与壁钟瞬态但机械状态分歧仍失败()
    {
        var live = JsonSerializer.SerializeToElement(new
        {
            tick = 193,
            phase = "Main",
            randomSeq = 42,
            inactivityActivePlayer = 1,
            inactivityWarningActive = true,
            inactivityLossRemainingMs = 120_000,
            inactivitySyncUtc = DateTime.UtcNow,
            operationClockActivePlayer = 1,
            operationClockSyncUtc = DateTime.UtcNow,
            operationClockPaused = false,
        });
        var recoveredOffline = JsonSerializer.SerializeToElement(new
        {
            tick = 190,
            phase = "Main",
            randomSeq = 42,
            inactivityActivePlayer = -1,
            inactivityWarningActive = false,
            inactivityLossRemainingMs = 240_000,
            inactivitySyncUtc = (DateTime?)null,
            operationClockActivePlayer = -1,
            operationClockSyncUtc = (DateTime?)null,
            operationClockPaused = true,
        });
        var mechanicallyDifferent = JsonSerializer.SerializeToElement(new
        {
            tick = 190,
            phase = "End",
            randomSeq = 42,
            inactivityActivePlayer = -1,
            inactivityWarningActive = false,
            inactivityLossRemainingMs = 240_000,
            inactivitySyncUtc = (DateTime?)null,
            operationClockActivePlayer = -1,
            operationClockSyncUtc = (DateTime?)null,
            operationClockPaused = true,
        });

        var liveHash = RoomRecoverySnapshotStore.ComputeRecoveryComparableStateSha256(live);
        Assert.Equal(
            liveHash,
            RoomRecoverySnapshotStore.ComputeRecoveryComparableStateSha256(recoveredOffline));
        Assert.NotEqual(
            liveHash,
            RoomRecoverySnapshotStore.ComputeRecoveryComparableStateSha256(mechanicallyDifferent));
    }

    [Fact]
    public async Task 恢复日志队列满时关键追加施加背压且零丢弃()
    {
        var root = TestDirectory("queue-pressure");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "queue.jsonl");
        var writer = new AsyncJsonlWriter(capacity: 256);
        try
        {
            writer.OpenAndAppendDurable("room", path, append: false, new { kind = "create" });
            for (var sequence = 1; sequence <= 10_000; sequence++)
                writer.AppendRequired("room", new { kind = "action", journalSequence = sequence });
            writer.Close("room");

            Assert.Equal(0, writer.DroppedEntries);
            Assert.True(writer.MaxQueueDepth >= 256, $"队列最大深度仅为 {writer.MaxQueueDepth}");
            Assert.Equal(10_001, (await File.ReadAllLinesAsync(path)).Length);
        }
        finally
        {
            writer.Shutdown();
            TryDelete(root);
        }
    }

    private static GameRoomManager.RoomEntry CreateRoom()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var deck = BuildLegalDeck("OP15-001");
        return GameRoomManager.CreateRoom(
            $"recovery-s0-{suffix}", $"recovery-a-{suffix}", deck,
            $"recovery-s1-{suffix}", $"recovery-b-{suffix}", deck,
            p0First: true,
            matchKind: MatchKind.Ranked,
            broadcastInitialState: false);
    }

    private static object BuildHeader(string roomId)
    {
        var deck = BuildLegalDeck("OP15-001");
        return new
        {
            kind = "create",
            roomId,
            seed = 123456,
            firstPlayer = 0,
            rulesetId = "builtin-test",
            openingSetupAfterFirstPlayerChoice = false,
            p0 = new { account = $"a-{roomId}", displayName = "恢复玩家A", deckRaw = deck },
            p1 = new { account = $"b-{roomId}", displayName = "恢复玩家B", deckRaw = deck },
            vsBot = false,
            matchKind = MatchKind.Ranked.ToString(),
            createdAtUtc = DateTime.UtcNow,
        };
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "房间没有在预期时间内进入恢复安全暂停");
    }

    private static string TestDirectory(string name)
        => OperatingSystem.IsWindows()
            ? Path.Combine(@"E:\GrandUMI-Temp\Tests", $"recovery-{name}-{Guid.NewGuid():N}")
            : Path.Combine(Path.GetTempPath(), $"grandumi-recovery-{name}-{Guid.NewGuid():N}");

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
