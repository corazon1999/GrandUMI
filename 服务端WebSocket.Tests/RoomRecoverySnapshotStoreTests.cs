using System.Text.Json;
using GrandUMI.Game;
using Xunit;

namespace GrandUMIServer.Tests;

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

    private static string TestDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(@"E:\GrandUMI-Temp\Tests", $"room-snapshot-{Guid.NewGuid():N}");
        return Path.Combine(Path.GetTempPath(), $"grandumi-room-snapshot-{Guid.NewGuid():N}");
    }
}
