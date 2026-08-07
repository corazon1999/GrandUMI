using System.Collections.Concurrent;
using GrandUMI.Diagnostics;

namespace GrandUMI.Cluster;

public sealed record RoomPlacement(string RoomId, string NodeId, DateTime UpdatedAtUtc);

/// <summary>
/// 房间目录抽象。当前使用进程内实现；未来接入 Redis/数据库目录时，游戏引擎无需改动。
/// </summary>
public interface IRoomPlacementDirectory
{
    string LocalNodeId { get; }
    void RegisterLocal(string roomId);
    void Unregister(string roomId);
    bool TryResolve(string roomId, out RoomPlacement placement);
    IReadOnlyCollection<RoomPlacement> Snapshot();
}

public sealed class LocalRoomPlacementDirectory : IRoomPlacementDirectory
{
    private readonly ConcurrentDictionary<string, RoomPlacement> _rooms = new(StringComparer.Ordinal);

    public static LocalRoomPlacementDirectory Instance { get; } = new();
    public string LocalNodeId => BuildInfo.NodeId;

    public void RegisterLocal(string roomId)
        => _rooms[roomId] = new RoomPlacement(roomId, LocalNodeId, DateTime.UtcNow);

    public void Unregister(string roomId) => _rooms.TryRemove(roomId, out _);

    public bool TryResolve(string roomId, out RoomPlacement placement)
        => _rooms.TryGetValue(roomId, out placement!);

    public IReadOnlyCollection<RoomPlacement> Snapshot() => _rooms.Values.ToArray();
}
