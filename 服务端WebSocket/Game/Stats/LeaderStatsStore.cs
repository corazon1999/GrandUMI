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

public sealed record LeaderMatchupItem(
    int Rank,
    string LeaderNumber,
    int Games,
    int? Wins,
    int? Losses,
    double? WinRate,
    int FirstGames,
    double? FirstWinRate,
    int SecondGames,
    double? SecondWinRate,
    bool IsMirror);

public sealed record LeaderMatchupSnapshot(
    string Period,
    DateTime GeneratedAtUtc,
    DateTime? SinceUtc,
    string LeaderNumber,
    IReadOnlyList<LeaderMatchupItem> Items);

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
    private readonly string _leaderboardDatabasePath;
    private readonly string _writeConnectionString;
    private readonly string _leaderboardConnectionString;
    private bool _initialized;

    public static LeaderStatsStore Default { get; } = new();

    public LeaderStatsStore(string? databasePath = null, string? leaderboardDatabasePath = null)
    {
        _databasePath = Path.GetFullPath(databasePath ?? ResolveDefaultDatabasePath());
        var configuredLeaderboardPath = leaderboardDatabasePath;
        if (string.IsNullOrWhiteSpace(configuredLeaderboardPath) && databasePath is null)
            configuredLeaderboardPath = Environment.GetEnvironmentVariable("GRANDUMI_LEADER_STATS_READ_PATH");
        _leaderboardDatabasePath = Path.GetFullPath(configuredLeaderboardPath ?? _databasePath);

        _writeConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
        _leaderboardConnectionString = string.Equals(
            _databasePath,
            _leaderboardDatabasePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? _writeConnectionString
            : new SqliteConnectionStringBuilder
            {
                DataSource = _leaderboardDatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString();
    }

    public string DatabasePath => _databasePath;
    public string LeaderboardDatabasePath => _leaderboardDatabasePath;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;

            var parent = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            using var connection = OpenWriteConnection();
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

            using var connection = OpenWriteConnection();
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
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();

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

    /// <summary>统计指定 Leader 对阵当前周期排行榜前十名的表现。</summary>
    public LeaderMatchupSnapshot GetMatchups(string leaderNumber, string? requestedPeriod, DateTime? nowUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderNumber);
        var normalizedLeader = leaderNumber.Trim();
        var leaderboard = GetLeaderboard(requestedPeriod, nowUtc);
        var topLeaders = leaderboard.Items
            .Where(x => x.Rank is not null)
            .Take(10)
            .ToArray();

        lock (_lock)
        {
            Initialize();
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            var rows = ReadMatchupRows(connection, normalizedLeader, leaderboard.SinceUtc);
            var mirror = ReadMirrorRow(connection, normalizedLeader, leaderboard.SinceUtc);
            var items = topLeaders.Select(opponent =>
            {
                var rank = opponent.Rank!.Value;
                if (string.Equals(opponent.LeaderNumber, normalizedLeader, StringComparison.Ordinal))
                {
                    return new LeaderMatchupItem(
                        rank,
                        opponent.LeaderNumber,
                        mirror.Games,
                        null,
                        null,
                        null,
                        mirror.Games,
                        mirror.Games == 0 ? null : mirror.FirstWins / (double)mirror.Games,
                        mirror.Games,
                        mirror.Games == 0 ? null : mirror.SecondWins / (double)mirror.Games,
                        true);
                }

                if (!rows.TryGetValue(opponent.LeaderNumber, out var row))
                    row = new MatchupAggregateRow(opponent.LeaderNumber, 0, 0, 0, 0, 0, 0);

                return new LeaderMatchupItem(
                    rank,
                    opponent.LeaderNumber,
                    row.Games,
                    row.Wins,
                    row.Games - row.Wins,
                    row.Games == 0 ? null : row.Wins / (double)row.Games,
                    row.FirstGames,
                    row.FirstGames == 0 ? null : row.FirstWins / (double)row.FirstGames,
                    row.SecondGames,
                    row.SecondGames == 0 ? null : row.SecondWins / (double)row.SecondGames,
                    false);
            }).ToArray();

            return new LeaderMatchupSnapshot(
                leaderboard.Period,
                leaderboard.GeneratedAtUtc,
                leaderboard.SinceUtc,
                normalizedLeader,
                items);
        }
    }

    /// <summary>回填工具用于按对局 ID 跳过已经导入的日志。</summary>
    public bool ContainsMatch(string matchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
        lock (_lock)
        {
            Initialize();
            using var connection = OpenWriteConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM match_results WHERE match_id = $matchId LIMIT 1;";
            command.Parameters.AddWithValue("$matchId", matchId);
            return command.ExecuteScalar() is not null;
        }
    }

    private SqliteConnection OpenWriteConnection()
    {
        var connection = new SqliteConnection(_writeConnectionString);
        connection.Open();
        return connection;
    }

    private SqliteConnection OpenLeaderboardConnection()
    {
        var connection = new SqliteConnection(_leaderboardConnectionString);
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

    private static Dictionary<string, MatchupAggregateRow> ReadMatchupRows(
        SqliteConnection connection,
        string leaderNumber,
        DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH filtered AS (
                SELECT player0_leader, player1_leader, winner_index, first_player_index
                FROM match_results
                WHERE counted = 1
                  AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
                  AND (player0_leader = $leaderNumber OR player1_leader = $leaderNumber)
            ),
            appearances AS (
                SELECT
                    player1_leader AS opponent_leader,
                    CASE WHEN winner_index = 0 THEN 1 ELSE 0 END AS won,
                    CASE WHEN first_player_index = 0 THEN 1 ELSE 0 END AS went_first
                FROM filtered
                WHERE player0_leader = $leaderNumber
                  AND player1_leader <> $leaderNumber
                UNION ALL
                SELECT
                    player0_leader AS opponent_leader,
                    CASE WHEN winner_index = 1 THEN 1 ELSE 0 END AS won,
                    CASE WHEN first_player_index = 1 THEN 1 ELSE 0 END AS went_first
                FROM filtered
                WHERE player1_leader = $leaderNumber
                  AND player0_leader <> $leaderNumber
            )
            SELECT
                opponent_leader,
                COUNT(*) AS games,
                SUM(won) AS wins,
                SUM(went_first) AS first_games,
                SUM(CASE WHEN went_first = 1 AND won = 1 THEN 1 ELSE 0 END) AS first_wins,
                SUM(CASE WHEN went_first = 0 THEN 1 ELSE 0 END) AS second_games,
                SUM(CASE WHEN went_first = 0 AND won = 1 THEN 1 ELSE 0 END) AS second_wins
            FROM appearances
            GROUP BY opponent_leader;
            """;
        command.Parameters.AddWithValue("$leaderNumber", leaderNumber);
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));

        var rows = new Dictionary<string, MatchupAggregateRow>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new MatchupAggregateRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6));
            rows[row.LeaderNumber] = row;
        }
        return rows;
    }

    private static MirrorAggregateRow ReadMirrorRow(
        SqliteConnection connection,
        string leaderNumber,
        DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*) AS games,
                COALESCE(SUM(CASE WHEN winner_index = first_player_index THEN 1 ELSE 0 END), 0) AS first_wins,
                COALESCE(SUM(CASE WHEN winner_index <> first_player_index THEN 1 ELSE 0 END), 0) AS second_wins
            FROM match_results
            WHERE counted = 1
              AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
              AND player0_leader = $leaderNumber
              AND player1_leader = $leaderNumber;
            """;
        command.Parameters.AddWithValue("$leaderNumber", leaderNumber);
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new MirrorAggregateRow(0, 0, 0);
        return new MirrorAggregateRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
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

    private sealed record MatchupAggregateRow(
        string LeaderNumber,
        int Games,
        int Wins,
        int FirstGames,
        int FirstWins,
        int SecondGames,
        int SecondWins);

    private sealed record MirrorAggregateRow(int Games, int FirstWins, int SecondWins);
}
