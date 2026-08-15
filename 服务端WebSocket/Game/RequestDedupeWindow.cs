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

    internal IReadOnlyList<RequestDedupeEntry> Snapshot(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        Prune(now);
        return _requests
            .Select(item => Parse(item.Key, item.Value))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.AcceptedAtUtc)
            .ToArray();
    }

    internal void Restore(IEnumerable<RequestDedupeEntry> entries, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var cutoff = now - ttl;
        foreach (var entry in entries)
        {
            if (entry.PlayerIndex is < 0 or > 1 || !IsTrackable(entry.RequestId) || entry.AcceptedAtUtc < cutoff)
                continue;
            _requests.TryAdd(Key(entry.PlayerIndex, entry.RequestId), entry.AcceptedAtUtc);
        }
        Prune(now);
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

    private static RequestDedupeEntry? Parse(string key, DateTime acceptedAtUtc)
    {
        var separator = key.IndexOf(':');
        return separator > 0 && int.TryParse(key[..separator], out var playerIndex)
            ? new RequestDedupeEntry(playerIndex, key[(separator + 1)..], acceptedAtUtc)
            : null;
    }
}

internal sealed record RequestDedupeEntry(int PlayerIndex, string RequestId, DateTime AcceptedAtUtc);
