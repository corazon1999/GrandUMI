using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace GrandUMI.Persistence;

public sealed record OnlinePlayerPeakPoint(string Date, int Peak);
public sealed record DailyActivePlayerPoint(string Date, int Count);
public sealed record PlayerTrafficSnapshot(
    int? CurrentOnlineCount,
    IReadOnlyList<OnlinePlayerPeakPoint> Peaks,
    IReadOnlyList<DailyActivePlayerPoint> DailyActivePlayers);

/// <summary>按 UTC+8 自然日持久化在线玩家峰值。</summary>
public sealed class OnlinePlayerHistoryStore
{
    private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly object _activityCacheGate = new();
    private readonly HashSet<string> _activityCache = new(StringComparer.Ordinal);
    private string? _activityCacheDate;

    public OnlinePlayerHistoryStore(string databasePath, bool readOnly = false)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
        IsReadOnly = readOnly;
    }

    public string DatabasePath => _databasePath;
    public bool IsReadOnly { get; }

    public void Initialize()
    {
        if (IsReadOnly)
            throw new InvalidOperationException("只读在线峰值数据源不能执行初始化。");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS online_player_daily_peaks (
                local_date TEXT PRIMARY KEY,
                peak_count INTEGER NOT NULL CHECK (peak_count >= 0),
                updated_at_utc INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS online_player_current (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                online_count INTEGER NOT NULL CHECK (online_count >= 0),
                observed_at_utc INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS daily_active_players (
                local_date TEXT NOT NULL,
                player_key TEXT NOT NULL,
                recorded_at_utc INTEGER NOT NULL,
                PRIMARY KEY(local_date, player_key)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS daily_active_counts (
                local_date TEXT PRIMARY KEY,
                active_count INTEGER NOT NULL CHECK (active_count >= 0),
                updated_at_utc INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Record(int count, DateTimeOffset? observedAt = null)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("只读在线峰值数据源不能记录数据。");
        count = Math.Max(0, count);
        var timestamp = observedAt ?? DateTimeOffset.UtcNow;
        var localDate = TimeZoneInfo.ConvertTime(timestamp, DisplayTimeZone).ToString("yyyy-MM-dd");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO online_player_daily_peaks(local_date, peak_count, updated_at_utc)
            VALUES ($date, $count, $updated)
            ON CONFLICT(local_date) DO UPDATE SET
                peak_count = MAX(online_player_daily_peaks.peak_count, excluded.peak_count),
                updated_at_utc = excluded.updated_at_utc;
            DELETE FROM online_player_daily_peaks
            WHERE local_date < $retentionStart;
            INSERT INTO online_player_current(singleton, online_count, observed_at_utc)
            VALUES (1, $count, $updated)
            ON CONFLICT(singleton) DO UPDATE SET
                online_count = excluded.online_count,
                observed_at_utc = excluded.observed_at_utc;
            """;
        command.Parameters.AddWithValue("$date", localDate);
        command.Parameters.AddWithValue("$count", count);
        command.Parameters.AddWithValue("$updated", timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$retentionStart", TimeZoneInfo.ConvertTime(timestamp, DisplayTimeZone).Date.AddDays(-45).ToString("yyyy-MM-dd"));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 记录成功登录的玩家。账号仅以不可逆摘要落库；同一玩家同一 UTC+8 自然日只计一次。
    /// 返回本次新增加的去重玩家数。
    /// </summary>
    public int RecordActivePlayers(IEnumerable<string> accounts, DateTimeOffset? observedAt = null)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("只读在线统计数据源不能记录日活玩家。");
        ArgumentNullException.ThrowIfNull(accounts);

        var timestamp = observedAt ?? DateTimeOffset.UtcNow;
        var localDate = LocalDate(timestamp);
        var playerKeys = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(HashAccount)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (playerKeys.Length == 0) return 0;

        string[] candidates;
        lock (_activityCacheGate)
        {
            if (!string.Equals(_activityCacheDate, localDate, StringComparison.Ordinal))
            {
                _activityCacheDate = localDate;
                _activityCache.Clear();
            }
            candidates = playerKeys.Where(_activityCache.Add).ToArray();
        }
        if (candidates.Length == 0) return 0;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var inserted = 0;
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR IGNORE INTO daily_active_players(local_date, player_key, recorded_at_utc)
                    VALUES ($date, $playerKey, $recordedAt);
                    """;
                var dateParameter = insert.Parameters.Add("$date", SqliteType.Text);
                var playerParameter = insert.Parameters.Add("$playerKey", SqliteType.Text);
                var recordedAtParameter = insert.Parameters.Add("$recordedAt", SqliteType.Integer);
                dateParameter.Value = localDate;
                recordedAtParameter.Value = timestamp.ToUnixTimeMilliseconds();
                foreach (var playerKey in candidates)
                {
                    playerParameter.Value = playerKey;
                    inserted += insert.ExecuteNonQuery();
                }
            }

            if (inserted > 0)
            {
                using var aggregate = connection.CreateCommand();
                aggregate.Transaction = transaction;
                aggregate.CommandText = """
                    INSERT INTO daily_active_counts(local_date, active_count, updated_at_utc)
                    VALUES ($date, $count, $updatedAt)
                    ON CONFLICT(local_date) DO UPDATE SET
                        active_count = daily_active_counts.active_count + excluded.active_count,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                aggregate.Parameters.AddWithValue("$date", localDate);
                aggregate.Parameters.AddWithValue("$count", inserted);
                aggregate.Parameters.AddWithValue("$updatedAt", timestamp.ToUnixTimeMilliseconds());
                aggregate.ExecuteNonQuery();
            }

            using (var retention = connection.CreateCommand())
            {
                retention.Transaction = transaction;
                retention.CommandText = """
                    DELETE FROM daily_active_players WHERE local_date < $retentionStart;
                    DELETE FROM daily_active_counts WHERE local_date < $retentionStart;
                    """;
                retention.Parameters.AddWithValue("$retentionStart", RetentionStart(timestamp));
                retention.ExecuteNonQuery();
            }
            transaction.Commit();
            return inserted;
        }
        catch
        {
            lock (_activityCacheGate)
            {
                if (string.Equals(_activityCacheDate, localDate, StringComparison.Ordinal))
                    foreach (var playerKey in candidates) _activityCache.Remove(playerKey);
            }
            throw;
        }
    }

    public bool RecordActivePlayer(string account, DateTimeOffset? observedAt = null)
        => RecordActivePlayers([account], observedAt) > 0;

    public int? GetCurrentOnlineCount(DateTimeOffset? now = null, TimeSpan? maxAge = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var freshness = maxAge ?? TimeSpan.FromMinutes(2);
        if (freshness <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT online_count
            FROM online_player_current
            WHERE singleton = 1 AND observed_at_utc >= $freshAfter;
            """;
        command.Parameters.AddWithValue("$freshAfter", timestamp.Subtract(freshness).ToUnixTimeMilliseconds());
        return command.ExecuteScalar() is long count ? checked((int)count) : null;
    }

    public IReadOnlyList<OnlinePlayerPeakPoint> GetRecentDailyPeaks(int days, DateTimeOffset? now = null)
    {
        if (days is < 1 or > 45) throw new ArgumentOutOfRangeException(nameof(days));
        var today = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, DisplayTimeZone).Date;
        var firstDate = today.AddDays(-(days - 1));
        var peaks = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT local_date, peak_count
                FROM online_player_daily_peaks
                WHERE local_date >= $firstDate AND local_date <= $lastDate
                ORDER BY local_date;
                """;
            command.Parameters.AddWithValue("$firstDate", firstDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$lastDate", today.ToString("yyyy-MM-dd"));
            using var reader = command.ExecuteReader();
            while (reader.Read()) peaks[reader.GetString(0)] = reader.GetInt32(1);
        }

        return Enumerable.Range(0, days)
            .Select(offset => firstDate.AddDays(offset).ToString("yyyy-MM-dd"))
            .Select(date => new OnlinePlayerPeakPoint(date, peaks.GetValueOrDefault(date)))
            .ToArray();
    }

    public IReadOnlyList<DailyActivePlayerPoint> GetRecentDailyActivePlayers(int days, DateTimeOffset? now = null)
    {
        if (days is < 1 or > 45) throw new ArgumentOutOfRangeException(nameof(days));
        var today = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, DisplayTimeZone).Date;
        var firstDate = today.AddDays(-(days - 1));
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT local_date, active_count
                FROM daily_active_counts
                WHERE local_date >= $firstDate AND local_date <= $lastDate
                ORDER BY local_date;
                """;
            command.Parameters.AddWithValue("$firstDate", firstDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$lastDate", today.ToString("yyyy-MM-dd"));
            using var reader = command.ExecuteReader();
            while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return Enumerable.Range(0, days)
            .Select(offset => firstDate.AddDays(offset).ToString("yyyy-MM-dd"))
            .Select(date => new DailyActivePlayerPoint(date, counts.GetValueOrDefault(date)))
            .ToArray();
    }

    public PlayerTrafficSnapshot GetSnapshot(int days, DateTimeOffset? now = null, TimeSpan? currentMaxAge = null)
        => new(
            GetCurrentOnlineCount(now, currentMaxAge),
            GetRecentDailyPeaks(days, now),
            GetRecentDailyActivePlayers(days, now));

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string HashAccount(string account)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant())));

    private static string LocalDate(DateTimeOffset timestamp)
        => TimeZoneInfo.ConvertTime(timestamp, DisplayTimeZone).ToString("yyyy-MM-dd");

    private static string RetentionStart(DateTimeOffset timestamp)
        => TimeZoneInfo.ConvertTime(timestamp, DisplayTimeZone).Date.AddDays(-45).ToString("yyyy-MM-dd");

    private static TimeZoneInfo ResolveDisplayTimeZone()
    {
        foreach (var id in new[] { "Asia/Singapore", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
    }
}
