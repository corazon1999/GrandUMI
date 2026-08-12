using System.Text.Json;

namespace GrandUMI;

/// <summary>维护模式的线程安全状态，以及新对局创建的原子准入门禁。</summary>
public sealed class GameMaintenanceState
{
    public const string PlayerMessage = "维护更新中，暂时无法开始新的对局";

    private readonly object _gate = new();
    private readonly string? _persistencePath;
    private bool _enabled;
    private DateTimeOffset? _startedAt;
    private int _pendingRoomCreations;

    public GameMaintenanceState(string? persistencePath = null)
    {
        _persistencePath = string.IsNullOrWhiteSpace(persistencePath)
            ? null
            : Path.GetFullPath(persistencePath);
        Load();
    }

    public MaintenanceSnapshot GetSnapshot(int activeRoomCount)
    {
        lock (_gate)
            return SnapshotLocked(activeRoomCount);
    }

    public MaintenanceSnapshot SetEnabled(bool enabled, int activeRoomCount)
    {
        lock (_gate)
        {
            if (_enabled != enabled)
            {
                var previousEnabled = _enabled;
                var previousStartedAt = _startedAt;
                _enabled = enabled;
                _startedAt = enabled ? DateTimeOffset.UtcNow : null;
                try
                {
                    SaveLocked();
                }
                catch
                {
                    _enabled = previousEnabled;
                    _startedAt = previousStartedAt;
                    throw;
                }
            }
            return SnapshotLocked(activeRoomCount);
        }
    }

    /// <summary>
    /// 与启用维护共用同一把锁，保证维护生效后不会再有新对局越过最终准入点。
    /// 已经取得准入权、尚未完成注册的房间会计入排空数量。
    /// </summary>
    public bool TryReserveRoomCreation(int activeRoomCount, int maximumRooms, out string? rejectionReason)
    {
        lock (_gate)
        {
            if (_enabled)
            {
                rejectionReason = PlayerMessage;
                return false;
            }
            if (activeRoomCount + _pendingRoomCreations >= maximumRooms)
            {
                rejectionReason = "服务器暂时无法创建新对局：room_limit";
                return false;
            }

            _pendingRoomCreations++;
            rejectionReason = null;
            return true;
        }
    }

    public MaintenanceSnapshot CompleteRoomCreation(int activeRoomCount)
    {
        lock (_gate)
        {
            if (_pendingRoomCreations > 0) _pendingRoomCreations--;
            return SnapshotLocked(activeRoomCount);
        }
    }

    private MaintenanceSnapshot SnapshotLocked(int activeRoomCount)
        => new(
            _enabled,
            Math.Max(0, activeRoomCount) + _pendingRoomCreations,
            _startedAt);

    private void Load()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath)) return;
        try
        {
            var stored = JsonSerializer.Deserialize<PersistedMaintenanceState>(File.ReadAllText(_persistencePath));
            _enabled = stored?.Enabled == true;
            _startedAt = _enabled ? stored?.StartedAt ?? DateTimeOffset.UtcNow : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[维护模式] 无法读取持久化状态，将按关闭处理：{ex.Message}");
            _enabled = false;
            _startedAt = null;
        }
    }

    private void SaveLocked()
    {
        if (_persistencePath is null) return;
        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = _persistencePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new PersistedMaintenanceState(_enabled, _startedAt)));
        File.Move(temporaryPath, _persistencePath, overwrite: true);
    }

    private sealed record PersistedMaintenanceState(bool Enabled, DateTimeOffset? StartedAt);
}

public sealed record MaintenanceSnapshot(
    bool Enabled,
    int ActiveRoomCount,
    DateTimeOffset? StartedAt);

public sealed class GameMaintenanceException(string message) : InvalidOperationException(message);
