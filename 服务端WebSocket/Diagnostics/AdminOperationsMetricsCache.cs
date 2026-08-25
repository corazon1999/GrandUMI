using GrandUMI.Game.Stats;
using GrandUMI.Persistence;

namespace GrandUMI.Diagnostics;

public sealed record CachedDailyMatchCounts(
    IReadOnlyList<DailyMatchCountPoint> Points,
    DateTimeOffset RefreshedAt);

public sealed record CachedStorageHealth(
    StorageHealthSnapshot Snapshot,
    DateTimeOffset RefreshedAt,
    TimeSpan RefreshInterval);

public sealed record CachedPlayerTraffic(
    PlayerTrafficSnapshot Snapshot,
    DateTimeOffset RefreshedAt,
    TimeSpan RefreshInterval);

/// <summary>隔离管理页的低频指标读取，避免网页轮询反复查询事实表或磁盘。</summary>
public sealed class AdminOperationsMetricsCache
{
    public static readonly TimeSpan MatchRefreshInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PlayerTrafficRefreshInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan StorageRefreshInterval = TimeSpan.FromHours(3);

    private readonly object _gate = new();
    private readonly Func<int, DateTime?, IReadOnlyList<DailyMatchCountPoint>> _loadMatches;
    private readonly Func<int, DateTimeOffset, PlayerTrafficSnapshot> _loadPlayerTraffic;
    private readonly Func<StorageHealthSnapshot> _loadStorage;
    private CachedDailyMatchCounts? _matches;
    private CachedPlayerTraffic? _playerTraffic;
    private CachedStorageHealth? _storage;

    public AdminOperationsMetricsCache(
        LeaderStatsStore leaderStatsStore,
        OnlinePlayerHistoryStore playerTrafficStore,
        Func<StorageHealthSnapshot>? loadStorage = null)
        : this(
            leaderStatsStore.GetRecentDailyMatchCounts,
            (days, now) => playerTrafficStore.GetSnapshot(days, now),
            loadStorage ?? StorageHealth.GetCurrent)
    {
    }

    internal AdminOperationsMetricsCache(
        Func<int, DateTime?, IReadOnlyList<DailyMatchCountPoint>> loadMatches,
        Func<int, DateTimeOffset, PlayerTrafficSnapshot> loadPlayerTraffic,
        Func<StorageHealthSnapshot> loadStorage)
    {
        _loadMatches = loadMatches ?? throw new ArgumentNullException(nameof(loadMatches));
        _loadPlayerTraffic = loadPlayerTraffic ?? throw new ArgumentNullException(nameof(loadPlayerTraffic));
        _loadStorage = loadStorage ?? throw new ArgumentNullException(nameof(loadStorage));
    }

    public CachedDailyMatchCounts GetDailyMatchCounts(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_matches is not null && timestamp - _matches.RefreshedAt < MatchRefreshInterval)
                return _matches;
            _matches = new CachedDailyMatchCounts(
                _loadMatches(30, timestamp.UtcDateTime),
                timestamp);
            return _matches;
        }
    }

    public CachedStorageHealth GetStorageHealth(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_storage is not null && timestamp - _storage.RefreshedAt < StorageRefreshInterval)
                return _storage;
            _storage = new CachedStorageHealth(_loadStorage(), timestamp, StorageRefreshInterval);
            return _storage;
        }
    }

    public CachedPlayerTraffic GetPlayerTraffic(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_playerTraffic is not null && timestamp - _playerTraffic.RefreshedAt < PlayerTrafficRefreshInterval)
                return _playerTraffic;
            _playerTraffic = new CachedPlayerTraffic(
                _loadPlayerTraffic(30, timestamp),
                timestamp,
                PlayerTrafficRefreshInterval);
            return _playerTraffic;
        }
    }
}
