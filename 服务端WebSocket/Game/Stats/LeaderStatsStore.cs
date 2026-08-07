using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Game.Stats;

public sealed record LeaderMatchResult(
    string MatchId,
    DateTime EndedAtUtc,
    MatchKind MatchKind,
    string Player0Account,
    string Player1Account,
    string Player0Leader,
    string Player1Leader,
    int? WinnerIndex,
    int FirstPlayerIndex,
    int TurnCount,
    string FinishReason);

public sealed record LeaderLeaderboardItem(
    int? Rank,
    string LeaderNumber,
    int Games,
    int Wins,
    int Losses,
    double WinRate,
    double UsageRate,
    int FirstGames,
    double? FirstWinRate,
    int SecondGames,
    double? SecondWinRate,
    bool InsufficientSample);

public sealed record LeaderLeaderboardSnapshot(
    string Period,
    DateTime GeneratedAtUtc,
    DateTime? SinceUtc,
    int TotalMatches,
    int MinimumGames,
    IReadOnlyList<LeaderLeaderboardItem> Items);

/// <summary>
/// Leader 排行榜的逐局事实存储。以 match_id 幂等写入，榜单按时间窗口即时聚合。
/// </summary>
public sealed class LeaderStatsStore
{
    public const int MinimumRankedGames = 20;
    public const int MinimumCountedTurn = 8;
    public const int StatsVersion = 1;

    private readonly object _lock = new();
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _initialized;

    public static LeaderStatsStore Default { get; } = new();

    public LeaderStatsStore(string? databasePath = null)
    {
        _databasePath = Path.GetFullPath(databasePath ?? ResolveDefaultDatabasePath());
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;

            var parent = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS match_results (
                    match_id            TEXT PRIMARY KEY,
                    ended_at_utc         TEXT NOT NULL,
                    match_kind          TEXT NOT NULL,
                    player0_key          TEXT NOT NULL,
                    player1_key          TEXT NOT NULL,
                    player0_leader       TEXT NOT NULL,
                    player1_leader       TEXT NOT NULL,
                    winner_index         INTEGER NULL,
                    first_player_index   INTEGER NOT NULL,
                    turn_count           INTEGER NOT NULL,
                    finish_reason        TEXT NOT NULL,
                    counted              INTEGER NOT NULL,
                    exclude_reason       TEXT NULL,
                    stats_version        INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_match_results_counted_ended
                    ON match_results(counted, ended_at_utc);
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }

    /// <summary>写入一局；重复 match_id 返回 false，且不会重复累计。</summary>
    public bool RecordMatch(LeaderMatchResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result.MatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Player0Leader);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Player1Leader);

        lock (_lock)
        {
            Initialize();
            var (counted, excludeReason) = EvaluateEligibility(result);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO match_results (
                    match_id, ended_at_utc, match_kind,
                    player0_key, player1_key, player0_leader, player1_leader,
                    winner_index, first_player_index, turn_count, finish_reason,
                    counted, exclude_reason, stats_version
                ) VALUES (
                    $matchId, $endedAtUtc, $matchKind,
                    $player0Key, $player1Key, $player0Leader, $player1Leader,
                    $winnerIndex, $firstPlayerIndex, $turnCount, $finishReason,
                    $counted, $excludeReason, $statsVersion
                );
                """;
            command.Parameters.AddWithValue("$matchId", result.MatchId);
            command.Parameters.AddWithValue("$endedAtUtc", ToDatabaseUtc(result.EndedAtUtc));
            command.Parameters.AddWithValue("$matchKind", result.MatchKind.ToString());
            command.Parameters.AddWithValue("$player0Key", HashAccount(result.Player0Account));
            command.Parameters.AddWithValue("$player1Key", HashAccount(result.Player1Account));
            command.Parameters.AddWithValue("$player0Leader", result.Player0Leader);
            command.Parameters.AddWithValue("$player1Leader", result.Player1Leader);
            command.Parameters.AddWithValue("$winnerIndex", (object?)result.WinnerIndex ?? DBNull.Value);
            command.Parameters.AddWithValue("$firstPlayerIndex", result.FirstPlayerIndex);
            command.Parameters.AddWithValue("$turnCount", result.TurnCount);
            command.Parameters.AddWithValue("$finishReason", result.FinishReason ?? "");
            command.Parameters.AddWithValue("$counted", counted ? 1 : 0);
            command.Parameters.AddWithValue("$excludeReason", (object?)excludeReason ?? DBNull.Value);
            command.Parameters.AddWithValue("$statsVersion", StatsVersion);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public LeaderLeaderboardSnapshot GetLeaderboard(string? requestedPeriod, DateTime? nowUtc = null)
    {
        var period = NormalizePeriod(requestedPeriod);
        var generatedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        DateTime? sinceUtc = period switch
        {
            "7d" => generatedAtUtc.AddDays(-7),
            "30d" => generatedAtUtc.AddDays(-30),
            _ => null,
        };

        lock (_lock)
        {
            Initialize();
            using var connection = OpenConnection();

            var totalMatches = ReadTotalMatches(connection, sinceUtc);
            var rows = ReadLeaderboardRows(connection, sinceUtc);
            var ordered = rows
                .OrderBy(x => x.Games < MinimumRankedGames ? 1 : 0)
                .ThenByDescending(x => x.Games >= MinimumRankedGames ? x.WinRate : -1)
                .ThenByDescending(x => x.Games)
                .ThenByDescending(x => x.WinRate)
                .ThenBy(x => x.LeaderNumber, StringComparer.Ordinal)
                .ToList();

            var nextRank = 1;
            var items = ordered.Select(x =>
            {
                var insufficient = x.Games < MinimumRankedGames;
                int? rank = insufficient ? null : nextRank++;
                return new LeaderLeaderboardItem(
                    rank,
                    x.LeaderNumber,
                    x.Games,
                    x.Wins,
                    x.Games - x.Wins,
                    x.WinRate,
                    totalMatches == 0 ? 0 : x.Games / (2d * totalMatches),
                    x.FirstGames,
                    x.FirstGames == 0 ? null : x.FirstWins / (double)x.FirstGames,
                    x.SecondGames,
                    x.SecondGames == 0 ? null : x.SecondWins / (double)x.SecondGames,
                    insufficient);
            }).ToArray();

            return new LeaderLeaderboardSnapshot(
                period,
                generatedAtUtc,
                sinceUtc,
                totalMatches,
                MinimumRankedGames,
                items);
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static (bool Counted, string? ExcludeReason) EvaluateEligibility(LeaderMatchResult result)
    {
        if (result.MatchKind == MatchKind.Bot) return (false, "bot");
        if (result.WinnerIndex is not (0 or 1)) return (false, "no_winner");
        if (result.TurnCount < MinimumCountedTurn) return (false, "too_short");
        if (string.Equals(result.Player0Account, result.Player1Account, StringComparison.OrdinalIgnoreCase))
            return (false, "same_account");
        return (true, null);
    }

    private static int ReadTotalMatches(SqliteConnection connection, DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM match_results
            WHERE counted = 1
              AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc);
            """;
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<AggregateRow> ReadLeaderboardRows(SqliteConnection connection, DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH filtered AS (
                SELECT player0_leader, player1_leader, winner_index, first_player_index
                FROM match_results
                WHERE counted = 1
                  AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
            ),
            appearances AS (
                SELECT
                    player0_leader AS leader_number,
                    CASE WHEN winner_index = 0 THEN 1 ELSE 0 END AS won,
                    CASE WHEN first_player_index = 0 THEN 1 ELSE 0 END AS went_first,
                    CASE WHEN first_player_index = 1 THEN 1 ELSE 0 END AS went_second
                FROM filtered
                UNION ALL
                SELECT
                    player1_leader AS leader_number,
                    CASE WHEN winner_index = 1 THEN 1 ELSE 0 END AS won,
                    CASE WHEN first_player_index = 1 THEN 1 ELSE 0 END AS went_first,
                    CASE WHEN first_player_index = 0 THEN 1 ELSE 0 END AS went_second
                FROM filtered
            )
            SELECT
                leader_number,
                COUNT(*) AS games,
                SUM(won) AS wins,
                SUM(went_first) AS first_games,
                SUM(CASE WHEN went_first = 1 AND won = 1 THEN 1 ELSE 0 END) AS first_wins,
                SUM(went_second) AS second_games,
                SUM(CASE WHEN went_second = 1 AND won = 1 THEN 1 ELSE 0 END) AS second_wins
            FROM appearances
            GROUP BY leader_number;
            """;
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));

        var rows = new List<AggregateRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var games = reader.GetInt32(1);
            var wins = reader.GetInt32(2);
            rows.Add(new AggregateRow(
                reader.GetString(0),
                games,
                wins,
                games == 0 ? 0 : wins / (double)games,
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }
        return rows;
    }

    private static string NormalizePeriod(string? period)
        => period?.ToLowerInvariant() switch
        {
            "7d" => "7d",
            "30d" => "30d",
            "all" => "all",
            _ => "7d",
        };

    private static string HashAccount(string account)
    {
        var normalized = account.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string ToDatabaseUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string ResolveDefaultDatabasePath()
    {
        var configuredDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
            return Path.Combine(configuredDir, "leader-stats.db");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Data", "leader-stats.db");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Data", "leader-stats.db");
    }

    private sealed record AggregateRow(
        string LeaderNumber,
        int Games,
        int Wins,
        double WinRate,
        int FirstGames,
        int FirstWins,
        int SecondGames,
        int SecondWins);
}
