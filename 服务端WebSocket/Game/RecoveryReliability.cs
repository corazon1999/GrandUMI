namespace GrandUMI.Game;

internal enum RoomRecoveryAvailability
{
    Healthy = 0,
    Paused = 1,
    Quarantined = 2,
}

internal sealed class RecoveryPersistenceException : IOException
{
    internal RecoveryPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 动作提交期间暂存所有玩家/观战下行。恢复日志和必要检查点都成功后才释放；
/// 提交失败后永久阻断该房间的引擎下行，避免客户端把未落盘状态误认为已确认。
/// </summary>
internal sealed class RoomOutboundCommitGate
{
    private readonly object _gate = new();
    private List<Action>? _pending;
    private bool _flushing;
    private bool _blocked;

    internal bool Begin()
    {
        lock (_gate)
        {
            if (_blocked || _pending is not null || _flushing) return false;
            _pending = new List<Action>();
            return true;
        }
    }

    internal void Deliver(Action delivery)
    {
        lock (_gate)
        {
            if (_blocked) return;
            if (_pending is not null || _flushing)
            {
                (_pending ??= new List<Action>()).Add(delivery);
                return;
            }
        }
        delivery();
    }

    internal void Commit()
    {
        while (true)
        {
            Action[] batch;
            lock (_gate)
            {
                if (_blocked) return;
                _flushing = true;
                batch = _pending?.ToArray() ?? Array.Empty<Action>();
                _pending = new List<Action>();
                if (batch.Length == 0)
                {
                    _pending = null;
                    _flushing = false;
                    return;
                }
            }

            foreach (var delivery in batch) delivery();
        }
    }

    internal void AbortAndBlock()
    {
        lock (_gate)
        {
            _pending?.Clear();
            _pending = null;
            _flushing = false;
            _blocked = true;
        }
    }
}

internal static class RecoveryReliabilityHealth
{
    private static long _pausedTotal;
    private static long _quarantinedTotal;
    private static long _lastFailureUtcTicks;

    internal static long PausedTotal => Interlocked.Read(ref _pausedTotal);
    internal static long QuarantinedTotal => Interlocked.Read(ref _quarantinedTotal);
    internal static DateTime? LastFailureUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFailureUtcTicks);
            return ticks <= 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    internal static void RecordPaused()
    {
        Interlocked.Increment(ref _pausedTotal);
        Interlocked.Exchange(ref _lastFailureUtcTicks, DateTime.UtcNow.Ticks);
    }

    internal static void RecordQuarantined()
    {
        Interlocked.Increment(ref _quarantinedTotal);
        Interlocked.Exchange(ref _lastFailureUtcTicks, DateTime.UtcNow.Ticks);
    }
}
