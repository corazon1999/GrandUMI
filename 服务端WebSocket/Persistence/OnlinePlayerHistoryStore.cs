using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public sealed record OnlinePlayerPeakPoint(string Date, int Peak);

/// <summary>按 UTC+8 自然日持久化在线玩家峰值。</summary>
public sealed class OnlinePlayerHistoryStore
{
    private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();
    private readonly string _databasePath;
    private readonly string _connectionString;

    public OnlinePlayerHistoryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
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
            """;
        command.ExecuteNonQuery();
    }

    public void Record(int count, DateTimeOffset? observedAt = null)
    {
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
            """;
        command.Parameters.AddWithValue("$date", localDate);
        command.Parameters.AddWithValue("$count", count);
        command.Parameters.AddWithValue("$updated", timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$retentionStart", TimeZoneInfo.ConvertTime(timestamp, DisplayTimeZone).Date.AddDays(-45).ToString("yyyy-MM-dd"));
        command.ExecuteNonQuery();
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

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

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
