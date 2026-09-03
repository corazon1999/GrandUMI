using GrandUMI.Game;
using GrandUMI.Game.Logging;

namespace GrandUMI.Diagnostics;

public static class ServerCapacity
{
    internal readonly record struct MemoryPressureSnapshot(
        long MemoryLoadBytes,
        long HighMemoryLoadThresholdBytes);

    private static readonly Func<MemoryPressureSnapshot> ProductionMemoryPressureProvider =
        ReadProductionMemoryPressure;
    private static Func<MemoryPressureSnapshot>? _memoryPressureProviderForTesting;

    // 默认值按 2026-08-08 单节点压测保留约 20% 对局余量；扩容必须显式配置并重新压测。
    public static int MaxConnections { get; } = ReadPositiveInt("GRANDUMI_MAX_CONNECTIONS", 1_000);
    public static int MaxRooms { get; } = ReadPositiveInt("GRANDUMI_MAX_ROOMS", 400);

    internal static bool HasMemoryPressureProviderForTesting
        => Volatile.Read(ref _memoryPressureProviderForTesting) is not null;

    internal static void SetMemoryPressureProviderForTesting(
        Func<MemoryPressureSnapshot> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (Interlocked.CompareExchange(
                ref _memoryPressureProviderForTesting,
                provider,
                comparand: null) is not null)
        {
            throw new InvalidOperationException("测试内存压力提供器只能在测试程序集启动时设置一次。");
        }
    }

    internal static MemoryPressureSnapshot ResolveMemoryPressureSnapshotForTesting(
        Func<MemoryPressureSnapshot> productionProvider,
        Func<MemoryPressureSnapshot>? testProvider)
    {
        ArgumentNullException.ThrowIfNull(productionProvider);
        return (testProvider ?? productionProvider)();
    }

    internal static MemoryPressureSnapshot ReadEffectiveMemoryPressureForTesting()
        => ReadEffectiveMemoryPressure();

    public static bool IsOverloaded(out string reason)
    {
        if (!CanAcceptConnection(out reason)) return true;
        if (!CanCreateRoom(out reason)) return true;
        reason = "";
        return false;
    }

    public static bool CanAcceptConnection(out string reason)
    {
        if (WebSocketBridge.ConnectionCount >= MaxConnections)
        {
            reason = "connection_limit";
            return false;
        }
        return CheckSharedResources(out reason);
    }

    public static bool CanCreateRoom(out string reason)
    {
        if (GameRoomManager.RoomCount >= MaxRooms)
        {
            reason = "room_limit";
            return false;
        }
        return CheckSharedResources(out reason);
    }

    private static bool CheckSharedResources(out string reason)
    {
        var storage = StorageHealth.GetCurrent();
        if (!storage.Healthy)
        {
            reason = storage.Reason;
            return false;
        }

        if (RoomJournal.QueueDepth >= 6_000
            || MatchLogRecorder.QueueDepth >= 12_000)
        {
            reason = "persistence_backlog";
            return false;
        }

        var memory = ReadEffectiveMemoryPressure();
        if (memory.HighMemoryLoadThresholdBytes > 0
            && memory.MemoryLoadBytes >= memory.HighMemoryLoadThresholdBytes * 0.90)
        {
            reason = "memory_pressure";
            return false;
        }

        reason = "";
        return true;
    }

    private static MemoryPressureSnapshot ReadEffectiveMemoryPressure()
        => ResolveMemoryPressureSnapshotForTesting(
            ProductionMemoryPressureProvider,
            Volatile.Read(ref _memoryPressureProviderForTesting));

    private static MemoryPressureSnapshot ReadProductionMemoryPressure()
    {
        var memory = GC.GetGCMemoryInfo();
        return new MemoryPressureSnapshot(
            memory.MemoryLoadBytes,
            memory.HighMemoryLoadThresholdBytes);
    }

    private static int ReadPositiveInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;
}
