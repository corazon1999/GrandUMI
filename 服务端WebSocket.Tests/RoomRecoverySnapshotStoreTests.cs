using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using Xunit;

namespace GrandUMIServer.Tests;

[CollectionDefinition("持久化目录隔离", DisableParallelization = true)]
public sealed class PersistenceDirectoryCollectionDefinition;

[Collection("持久化目录隔离")]
public sealed class RoomRecoverySnapshotStoreTests
{
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

            await GameRoomManager.RestoreAll();
            var room = GameRoomManager.GetRoom(roomId);

            Assert.NotNull(room);
            Assert.All(room!.DisconnectedPlayers, Assert.True);
            Assert.All(room.DisconnectStartedAt, value => Assert.True(value > 0));
            Assert.True(room.Engine.State.OperationClockPaused);

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
