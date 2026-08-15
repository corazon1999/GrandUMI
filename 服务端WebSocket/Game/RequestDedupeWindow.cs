using System.Collections.Concurrent;

namespace GrandUMI.Game;

/// <summary>有界、带过期时间的对局动作幂等窗口。</summary>
internal sealed class RequestDedupeWindow(int capacity, TimeSpan ttl)
{
    private readonly ConcurrentDictionary<string, DateTime> _requests = new(StringComparer.Ordinal);

    internal int Count => _requests.Count;

    internal bool IsTrackable(string? requestId)
        => !string.IsNullOrWhiteSpace(requestId) && requestId.Trim().Length <= 128;

    internal bool TryRegister(int playerIndex, string? requestId, DateTime? utcNow = null)
    {
        if (!IsTrackable(requestId)) return true;
        var now = utcNow ?? DateTime.UtcNow;
        Prune(now);
        return _requests.TryAdd(Key(playerIndex, requestId!), now);
    }

    internal void Remove(int playerIndex, string? requestId)
    {
        if (IsTrackable(requestId)) _requests.TryRemove(Key(playerIndex, requestId!), out _);
    }

    private void Prune(DateTime utcNow)
    {
        if (_requests.Count < capacity) return;
        var cutoff = utcNow - ttl;
        foreach (var item in _requests)
        {
            if (item.Value < cutoff) _requests.TryRemove(item.Key, out _);
        }
        if (_requests.Count < capacity) return;
        foreach (var oldest in _requests.OrderBy(item => item.Value).Take(_requests.Count - capacity + 1))
            _requests.TryRemove(oldest.Key, out _);
    }

    private static string Key(int playerIndex, string requestId)
        => $"{playerIndex}:{requestId.Trim()}";
}
