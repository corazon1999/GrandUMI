using Xunit;

namespace GrandUMI.Tests;

public sealed class GameMaintenanceStateTests
{
    [Fact]
    public void EnablingMaintenanceAtomicallyBlocksNewRoomsAndTracksInflightCreation()
    {
        var state = new GameMaintenanceState();

        Assert.True(state.TryReserveRoomCreation(activeRoomCount: 2, maximumRooms: 10, out var firstReason));
        Assert.Null(firstReason);

        var enabled = state.SetEnabled(true, activeRoomCount: 2);
        Assert.True(enabled.Enabled);
        Assert.Equal(3, enabled.ActiveRoomCount);
        Assert.NotNull(enabled.StartedAt);

        Assert.False(state.TryReserveRoomCreation(activeRoomCount: 2, maximumRooms: 10, out var blockedReason));
        Assert.Equal(GameMaintenanceState.PlayerMessage, blockedReason);

        var drained = state.CompleteRoomCreation(activeRoomCount: 0);
        Assert.Equal(0, drained.ActiveRoomCount);

        var disabled = state.SetEnabled(false, activeRoomCount: 0);
        Assert.False(disabled.Enabled);
        Assert.Null(disabled.StartedAt);
        Assert.True(state.TryReserveRoomCreation(activeRoomCount: 0, maximumRooms: 10, out _));
    }

    [Fact]
    public void MaintenanceStateSurvivesServerRestart()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "maintenance-state.json");
        try
        {
            var firstProcess = new GameMaintenanceState(path);
            var enabled = firstProcess.SetEnabled(true, activeRoomCount: 4);

            var restartedProcess = new GameMaintenanceState(path);
            var restored = restartedProcess.GetSnapshot(activeRoomCount: 0);

            Assert.True(restored.Enabled);
            Assert.Equal(enabled.StartedAt, restored.StartedAt);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedPersistenceDoesNotChangeInMemoryState()
    {
        var directory = CreateTestDirectory();
        try
        {
            var state = new GameMaintenanceState(directory);

            Assert.ThrowsAny<Exception>(() => state.SetEnabled(true, activeRoomCount: 0));
            Assert.False(state.GetSnapshot(activeRoomCount: 0).Enabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        const string driveRoot = @"E:\";
        if (!Directory.Exists(driveRoot))
            throw new InvalidOperationException("E 盘不可用，维护模式测试不会回退到系统临时目录。");

        var directory = Path.Combine(
            driveRoot,
            "GrandUMI-Temp",
            "Tests",
            "maintenance-state",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
