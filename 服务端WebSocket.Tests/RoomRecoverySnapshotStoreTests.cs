using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMIServer.Tests;

[CollectionDefinition("持久化目录隔离", DisableParallelization = true)]
public sealed class PersistenceDirectoryCollectionDefinition;

[Collection("持久化目录隔离")]
public sealed class RoomRecoverySnapshotStoreTests
{
    [Fact]
    public async Task 旧版恢复检查点仍可读取请求去重窗口并由重放路径刷新()
    {
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        try
        {
            var state = JsonSerializer.SerializeToElement(new { tick = 16, phase = "主阶段" });
            var acceptedAtUtc = DateTime.UtcNow;
            var legacy = new RoomRecoverySnapshot(
                RoomRecoverySnapshotStore.MinimumCompatibleSchemaVersion,
                "legacy-room-snapshot-test",
                16,
                acceptedAtUtc,
                [15, 16],
                [600_000, 590_000],
                [new RequestDedupeEntry(0, "attach-don-request-16", acceptedAtUtc)],
                RoomRecoverySnapshotStore.ComputeStateSha256(state),
                state);

            RoomRecoverySnapshotStore.Capture(legacy);
            await RoomRecoverySnapshotStore.FlushAsync();

            var restored = Assert.IsType<RoomRecoverySnapshot>(
                RoomRecoverySnapshotStore.TryRead(legacy.RoomId));
            Assert.Equal(RoomRecoverySnapshotStore.MinimumCompatibleSchemaVersion, restored.SchemaVersion);
            var request = Assert.Single(restored.ProcessedRequests);
            Assert.Equal(0, request.PlayerIndex);
            Assert.Equal("attach-don-request-16", request.RequestId);

            var future = legacy with { SchemaVersion = RoomRecoverySnapshotStore.SchemaVersion + 1 };
            RoomRecoverySnapshotStore.Capture(future);
            await RoomRecoverySnapshotStore.FlushAsync();
            Assert.Null(RoomRecoverySnapshotStore.TryRead(future.RoomId));
            await RoomRecoverySnapshotStore.DeleteDeferred(legacy.RoomId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task 恢复快照原子写入后可读取并删除()
    {
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        try
        {
            var state = JsonSerializer.SerializeToElement(new { tick = 16, phase = "主阶段" });
            var snapshot = new RoomRecoverySnapshot(
                RoomRecoverySnapshotStore.SchemaVersion,
                "room-snapshot-test",
                16,
                DateTime.UtcNow,
                [15, 16],
                [600_000, 590_000],
                [new RequestDedupeEntry(0, "request-16", DateTime.UtcNow)],
                RoomRecoverySnapshotStore.ComputeStateSha256(state),
                state);

            RoomRecoverySnapshotStore.Capture(snapshot);
            await RoomRecoverySnapshotStore.FlushAsync();

            var restored = RoomRecoverySnapshotStore.TryRead(snapshot.RoomId);
            Assert.NotNull(restored);
            Assert.Equal(snapshot.JournalSequence, restored!.JournalSequence);
            Assert.Equal(snapshot.StateSha256, restored.StateSha256);
            Assert.Equal("request-16", Assert.Single(restored.ProcessedRequests).RequestId);

            await RoomRecoverySnapshotStore.DeleteDeferred(snapshot.RoomId);
            Assert.Null(RoomRecoverySnapshotStore.TryRead(snapshot.RoomId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task 重启恢复的离线房间_双方都会立即进入断线宽限计时()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"restore-{Guid.NewGuid():N}"[..20];
        try
        {
            var deck = BuildLegalDeck("OP15-001");
            var header = new
            {
                kind = "create",
                roomId,
                seed = 123456,
                firstPlayer = 0,
                openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account = $"restore-a-{roomId}", displayName = "恢复玩家A", deckRaw = deck },
                p1 = new { account = $"restore-b-{roomId}", displayName = "恢复玩家B", deckRaw = deck },
                vsBot = false,
                matchKind = MatchKind.Ranked.ToString(),
                createdAtUtc = DateTime.UtcNow,
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, $"{roomId}.jsonl"),
                JsonSerializer.Serialize(header) + Environment.NewLine);

            // v2 的私有状态没有建立时快照来源字段。升级后必须保留其请求去重窗口、
            // 跳过不可比较的旧结构哈希，并在动作重放成功后刷新为当前 v5。
            var legacyPrivateState = JsonSerializer.SerializeToElement(new { schema = 2, legacy = true });
            var legacyStateHash = RoomRecoverySnapshotStore.ComputeStateSha256(legacyPrivateState);
            RoomRecoverySnapshotStore.Capture(new RoomRecoverySnapshot(
                RoomRecoverySnapshotStore.SchemaVersion - 1,
                roomId,
                0,
                DateTime.UtcNow,
                [0, 0],
                [600_000, 600_000],
                [new RequestDedupeEntry(0, "legacy-v2-request", DateTime.UtcNow)],
                legacyStateHash,
                legacyPrivateState));
            await RoomRecoverySnapshotStore.FlushAsync();

            await GameRoomManager.RestoreAll();
            var room = GameRoomManager.GetRoom(roomId);

            Assert.NotNull(room);
            Assert.All(room!.DisconnectedPlayers, Assert.True);
            Assert.All(room.DisconnectStartedAt, value => Assert.True(value > 0));
            Assert.True(room.Engine.State.OperationClockPaused);
            Assert.All(room.Engine.State.OperationTurnExtensionUsed, Assert.False);
            Assert.Equal(240_000, room.Engine.State.InactivityLossRemainingMs);

            var refreshed = Assert.IsType<RoomRecoverySnapshot>(
                RoomRecoverySnapshotStore.TryRead(roomId));
            Assert.Equal(RoomRecoverySnapshotStore.SchemaVersion, refreshed.SchemaVersion);
            Assert.NotEqual(legacyStateHash, refreshed.StateSha256);
            Assert.Contains(
                refreshed.ProcessedRequests,
                request => request.PlayerIndex == 0 && request.RequestId == "legacy-v2-request");

            var graceField = typeof(GameRoomManager).GetField(
                "_grace", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var grace = (System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>)graceField.GetValue(null)!;
            Assert.Contains($"{roomId}:offline-0", grace.Keys);
            Assert.Contains($"{roomId}:offline-1", grace.Keys);
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task 重启恢复会保留一次性加时并兼容旧挂机字段()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"restore-clock-{Guid.NewGuid():N}"[..24];
        try
        {
            var deck = BuildLegalDeck("OP15-001");
            var now = DateTime.UtcNow;
            var header = new
            {
                kind = "create",
                roomId,
                seed = 123456,
                firstPlayer = 0,
                openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account = $"restore-a-{roomId}", displayName = "恢复玩家A", deckRaw = deck },
                p1 = new { account = $"restore-b-{roomId}", displayName = "恢复玩家B", deckRaw = deck },
                vsBot = false,
                matchKind = MatchKind.Ranked.ToString(),
                createdAtUtc = now,
            };
            var clock = new
            {
                kind = "clock",
                player0RemainingMs = 900_000,
                player1RemainingMs = 800_000,
                player0TurnRemainingMs = 520_000,
                player1TurnRemainingMs = 300_000,
                turnCount = 3,
                player0TurnExtensionUsed = true,
                player1TurnExtensionUsed = false,
                player0InactivityPenaltyMs = 75_000,
                player1InactivityPenaltyMs = 12_000,
                tsUtc = now,
            };
            await File.WriteAllLinesAsync(
                Path.Combine(root, $"{roomId}.jsonl"),
                [JsonSerializer.Serialize(header), JsonSerializer.Serialize(clock)]);

            await GameRoomManager.RestoreAll();
            var room = GameRoomManager.GetRoom(roomId);

            Assert.NotNull(room);
            Assert.Equal(new bool[] { true, false }, room!.Engine.State.OperationTurnExtensionUsed);
            Assert.Equal(new long[] { 480_000, 300_000 }, room.Engine.State.OperationTurnClockRemainingMs);
            Assert.Equal(3, room.Engine.State.OperationTurnClockTurnCount);
            Assert.All(room.DisconnectedPlayers, Assert.True);
            Assert.Equal(-1, room.Engine.State.InactivityActivePlayer);
            Assert.Equal(240_000, room.Engine.State.InactivityLossRemainingMs);
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task 服务重启从动作日志恢复待回应平局申请及Bug描述()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"restore-draw-{Guid.NewGuid():N}"[..23];
        try
        {
            var deck = BuildLegalDeck("OP15-001");
            var now = DateTime.UtcNow;
            var header = new
            {
                kind = "create",
                roomId,
                seed = 123456,
                firstPlayer = 0,
                openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account = $"restore-a-{roomId}", displayName = "恢复玩家A", deckRaw = deck },
                p1 = new { account = $"restore-b-{roomId}", displayName = "恢复玩家B", deckRaw = deck },
                vsBot = false,
                matchKind = MatchKind.Ranked.ToString(),
                createdAtUtc = now,
            };
            var requestDraw = new
            {
                kind = "action",
                journalSequence = 1,
                playerIndex = 0,
                action = "RequestDraw",
                data = new { description = "  服务重启后仍要展示这段描述  " },
                requestId = "draw-request-1",
                operationSequence = 1,
                tsUtc = now,
            };
            await File.WriteAllLinesAsync(
                Path.Combine(root, $"{roomId}.jsonl"),
                [JsonSerializer.Serialize(header), JsonSerializer.Serialize(requestDraw)]);

            await GameRoomManager.RestoreAll();
            var room = GameRoomManager.GetRoom(roomId);

            Assert.NotNull(room);
            Assert.Equal(0, room!.Engine.State.PendingDrawRequester);
            Assert.Equal("服务重启后仍要展示这段描述", room.Engine.State.PendingDrawRequestDescription);

            var opponentResync = JsonSerializer.SerializeToElement(
                StateSnapshotBuilder.Build(room.Engine.State, viewerIndex: 1));
            Assert.True(opponentResync.GetProperty("drawRequestPendingFromOpponent").GetBoolean());
            Assert.Equal("服务重启后仍要展示这段描述",
                opponentResync.GetProperty("drawRequestDescription").GetString());

            var privateState = JsonSerializer.SerializeToElement(
                PrivateStateSnapshotBuilder.Build(room.Engine.State));
            Assert.Equal("服务重启后仍要展示这段描述",
                privateState.GetProperty("pendingDrawRequestDescription").GetString());
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task 服务重启后反馈证据恢复最近已接受动作但不恢复动作数据()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory();
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", root);
        var roomId = $"restore-feedback-{Guid.NewGuid():N}"[..27];
        var account0 = $"restore-feedback-a-{roomId}";
        var account1 = $"restore-feedback-b-{roomId}";
        try
        {
            var deck = BuildLegalDeck("OP15-001");
            var now = DateTime.UtcNow;
            var header = new
            {
                kind = "create",
                roomId,
                seed = 123456,
                firstPlayer = 0,
                openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account = account0, displayName = "恢复玩家A", deckRaw = deck },
                p1 = new { account = account1, displayName = "恢复玩家B", deckRaw = deck },
                vsBot = false,
                matchKind = MatchKind.Ranked.ToString(),
                createdAtUtc = now,
            };
            var acceptedAction = new
            {
                kind = "action",
                journalSequence = 1,
                playerIndex = 0,
                action = "RequestDraw",
                data = new { description = "不能进入反馈证据的私有动作数据 OP15-001" },
                requestId = "restored-request-1",
                operationSequence = 1,
                tsUtc = now,
            };
            await File.WriteAllLinesAsync(
                Path.Combine(root, $"{roomId}.jsonl"),
                [JsonSerializer.Serialize(header), JsonSerializer.Serialize(acceptedAction)]);

            await GameRoomManager.RestoreAll();
            var room = Assert.IsType<GameRoomManager.RoomEntry>(GameRoomManager.GetRoom(roomId));
            var reboundSession = $"restore-feedback-session-{Guid.NewGuid():N}";
            Assert.True(GameRoomManager.TryReclaim(reboundSession, account0));

            var authority = await GameRoomManager.CaptureFeedbackEvidenceAsync(reboundSession);
            using var document = JsonDocument.Parse(authority.ToJsonString());
            var evidence = document.RootElement;
            var actions = evidence.GetProperty("recentActions").EnumerateArray().ToArray();

            Assert.True(evidence.GetProperty("connection").GetProperty("restoredFromRecovery").GetBoolean());
            Assert.Contains(actions, action => action.GetProperty("action").GetString() == "RequestDraw"
                && action.GetProperty("outcome").GetString() == "accepted"
                && action.GetProperty("requestId").GetString() == "restored-request-1");
            Assert.DoesNotContain("不能进入反馈证据", evidence.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("OP15-001", evidence.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(account0, evidence.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(account1, evidence.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(30);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string TestDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(@"E:\GrandUMI-Temp\Tests", $"room-snapshot-{Guid.NewGuid():N}");
        return Path.Combine(Path.GetTempPath(), $"grandumi-room-snapshot-{Guid.NewGuid():N}");
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
