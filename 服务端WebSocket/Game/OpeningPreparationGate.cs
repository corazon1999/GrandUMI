namespace GrandUMI.Game;

/// <summary>仅用于开局传输准备，不参与卡牌规则；截止时间固定，完成信号只消费一次。</summary>
internal sealed class OpeningPreparationGate(int requiredMask, DateTime deadlineUtc)
{
    private readonly object _gate = new();
    private int _readyMask;
    private bool _completed;
    private int _timerStarted;
    private int _cancelled;
    private readonly CancellationTokenSource _cancellation = new();
    public DateTime DeadlineUtc { get; } = deadlineUtc;

    public bool TryStartTimer() => Interlocked.Exchange(ref _timerStarted, 1) == 0;
    public CancellationToken CancellationToken => _cancellation.Token;

    public bool TryReady(int playerIndex)
    {
        if (playerIndex is < 0 or > 1) return false;
        lock (_gate)
        {
            if (_completed) return false;
            _readyMask |= 1 << playerIndex;
            if ((_readyMask & requiredMask) != requiredMask) return false;
            _completed = true;
            return true;
        }
    }

    public bool TryExpire(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_completed || nowUtc < DeadlineUtc) return false;
            _completed = true;
            return true;
        }
    }

    public void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) != 0) return;
        _cancellation.Cancel();
        // Token 在等待任务结束前仍会被读取，由房间生命周期回收这个短期对象。
    }
}
