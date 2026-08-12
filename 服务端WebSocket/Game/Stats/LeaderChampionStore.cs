using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Game.Stats;

public sealed record LeaderChampion(
    string LeaderNumber,
    string PlayerKey,
    int Games,
    int Wins,
    double Score);

/// <summary>
/// Leader 专属称号的权威数据源。称号只从公开匹配的有效对局中产生，
/// 使用 30 日窗口和 Wilson 下置信界，避免少量高胜率对局刷榜。
/// </summary>
public sealed class LeaderChampionStore
{
    public const int MinimumChampionGames = 30;
    public const int ChampionWindowDays = 30;
    private const double WilsonZ = 1.645; // 90% lower confidence bound

    private readonly object _lock = new();
    private readonly string _databasePath;
    private readonly string _leaderboardDatabasePath;
    private readonly string _writeConnectionString;
    private readonly string _leaderboardConnectionString;
    private DateTime _cacheCreatedAtUtc;
    private IReadOnlyDictionary<string, LeaderChampion>? _champions;
    private bool _initialized;

    // 与 Leader 战绩事实表共用数据库：首次启用时可直接从已记录的有效对局计算称号。
    public static LeaderChampionStore Default { get; } = new(
        LeaderStatsStore.Default.DatabasePath,
        LeaderStatsStore.Default.LeaderboardDatabasePath);

    public LeaderChampionStore(string? databasePath = null, string? leaderboardDatabasePath = null)
    {
        _databasePath = Path.GetFullPath(databasePath ?? ResolveDefaultDatabasePath());
        _leaderboardDatabasePath = Path.GetFullPath(leaderboardDatabasePath ?? _databasePath);
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

                CREATE TABLE IF NOT EXISTS champion_match_results (
                    match_id            TEXT PRIMARY KEY,
                    ended_at_utc         TEXT NOT NULL,
                    player0_key          TEXT NOT NULL,
                    player1_key          TEXT NOT NULL,
                    player0_leader       TEXT NOT NULL,
                    player1_leader       TEXT NOT NULL,
                    winner_index         INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_champion_matches_ended
                    ON champion_match_results(ended_at_utc);
                """;
            command.ExecuteNonQuery();
            BackfillFromLeaderStats(connection);
            _initialized = true;
        }
    }

    public bool RecordMatch(LeaderMatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsEligible(result)) return false;

        lock (_lock)
        {
            Initialize();
            using var connection = OpenWriteConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO champion_match_results (
                    match_id, ended_at_utc, player0_key, player1_key,
                    player0_leader, player1_leader, winner_index
                ) VALUES (
                    $matchId, $endedAtUtc, $player0Key, $player1Key,
                    $player0Leader, $player1Leader, $winnerIndex
                );
                """;
            command.Parameters.AddWithValue("$matchId", result.MatchId);
            command.Parameters.AddWithValue("$endedAtUtc", ToDatabaseUtc(result.EndedAtUtc));
            command.Parameters.AddWithValue("$player0Key", HashAccount(result.Player0Account));
            command.Parameters.AddWithValue("$player1Key", HashAccount(result.Player1Account));
            command.Parameters.AddWithValue("$player0Leader", result.Player0Leader.Trim());
            command.Parameters.AddWithValue("$player1Leader", result.Player1Leader.Trim());
            command.Parameters.AddWithValue("$winnerIndex", result.WinnerIndex!.Value);
            var inserted = command.ExecuteNonQuery() == 1;
            if (inserted) _champions = null;
            return inserted;
        }
    }

    /// <summary>返回该玩家当前持有的全部 Leader 冠军称号，按称号强度排序。</summary>
    public IReadOnlyList<string> GetChampionLeaderNumbers(string? account, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(account)) return Array.Empty<string>();
        return GetChampionLeaderNumbersByPlayerKey(HashAccount(account), nowUtc);
    }

    /// <summary>供已持有匿名玩家键的服务端模块查询称号，避免重新暴露原始账号。</summary>
    internal IReadOnlyList<string> GetChampionLeaderNumbersByPlayerKey(string? playerKey, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(playerKey)) return Array.Empty<string>();
        return GetChampionLeaderNumbersByPlayerKeys(new[] { playerKey }, nowUtc)
            .GetValueOrDefault(playerKey.Trim(), Array.Empty<string>());
    }

    /// <summary>一次读取多名匿名玩家持有的称号，供排行榜批量组装响应。</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> GetChampionLeaderNumbersByPlayerKeys(
        IEnumerable<string> playerKeys,
        DateTime? nowUtc = null)
    {
        var keys = playerKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        return GetChampions(nowUtc)
            .Values
            .Where(x => keys.Contains(x.PlayerKey))
            .GroupBy(x => x.PlayerKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Games)
                    .ThenBy(x => x.LeaderNumber, StringComparer.Ordinal)
                    .Select(x => x.LeaderNumber)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public bool IsChampion(string? account, string? leaderNumber, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(leaderNumber)) return false;
        return GetChampions(nowUtc).TryGetValue(leaderNumber.Trim(), out var champion)
            && champion.PlayerKey == HashAccount(account);
    }

    public LeaderChampion? GetChampion(string? leaderNumber, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(leaderNumber)) return null;
        return GetChampions(nowUtc).TryGetValue(leaderNumber.Trim(), out var champion) ? champion : null;
    }

    private IReadOnlyDictionary<string, LeaderChampion> GetChampions(DateTime? nowUtc)
    {
        var generatedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        lock (_lock)
        {
            Initialize();
            if (nowUtc is null && _champions is not null && generatedAtUtc - _cacheCreatedAtUtc < TimeSpan.FromSeconds(15))
                return _champions;

            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("最强使用者排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT player_key, leader_number, COUNT(*) AS games, SUM(won) AS wins
                FROM (
                    SELECT player0_key AS player_key, player0_leader AS leader_number,
                           CASE WHEN winner_index = 0 THEN 1 ELSE 0 END AS won
                    FROM champion_match_results
                    WHERE ended_at_utc >= $sinceUtc
                    UNION ALL
                    SELECT player1_key AS player_key, player1_leader AS leader_number,
                           CASE WHEN winner_index = 1 THEN 1 ELSE 0 END AS won
                    FROM champion_match_results
                    WHERE ended_at_utc >= $sinceUtc
                )
                GROUP BY player_key, leader_number
                HAVING COUNT(*) >= $minimumGames;
                """;
            command.Parameters.AddWithValue("$sinceUtc", ToDatabaseUtc(generatedAtUtc.AddDays(-ChampionWindowDays)));
            command.Parameters.AddWithValue("$minimumGames", MinimumChampionGames);

            var candidates = new List<LeaderChampion>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var games = reader.GetInt32(2);
                var wins = reader.GetInt32(3);
                candidates.Add(new LeaderChampion(
                    reader.GetString(1),
                    reader.GetString(0),
                    games,
                    wins,
                    WilsonLowerBound(wins, games)));
            }

            var result = candidates
                .GroupBy(x => x.LeaderNumber, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.Games)
                        .ThenByDescending(x => x.Wins)
                        .ThenBy(x => x.PlayerKey, StringComparer.Ordinal)
                        .First(),
                    StringComparer.Ordinal);

            if (nowUtc is null)
            {
                _cacheCreatedAtUtc = generatedAtUtc;
                _champions = result;
            }
            return result;
        }
    }

    private static bool IsEligible(LeaderMatchResult result)
        => result.MatchKind is MatchKind.Ranked or MatchKind.Casual or MatchKind.Matchmaking
           && result.WinnerIndex is 0 or 1
           && result.TurnCount >= LeaderStatsStore.MinimumCountedTurn
           && !IsDisconnectFinish(result.FinishReason)
           && !string.Equals(result.Player0Account, result.Player1Account, StringComparison.OrdinalIgnoreCase);

    private static bool IsDisconnectFinish(string? finishReason)
        => !string.IsNullOrWhiteSpace(finishReason)
           && (finishReason.Contains("断线", StringComparison.Ordinal)
               || finishReason.Contains("disconnect", StringComparison.OrdinalIgnoreCase));

    private static double WilsonLowerBound(int wins, int games)
    {
        var rate = wins / (double)games;
        var z2 = WilsonZ * WilsonZ;
        return (rate + z2 / (2 * games) - WilsonZ * Math.Sqrt((rate * (1 - rate) + z2 / (4 * games)) / games))
            / (1 + z2 / games);
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

    private static void BackfillFromLeaderStats(SqliteConnection connection)
    {
        using var tableCheck = connection.CreateCommand();
        tableCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'match_results' LIMIT 1;";
        if (tableCheck.ExecuteScalar() is null) return;

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO champion_match_results (
                match_id, ended_at_utc, player0_key, player1_key,
                player0_leader, player1_leader, winner_index
            )
            SELECT match_id, ended_at_utc, player0_key, player1_key,
                   player0_leader, player1_leader, winner_index
            FROM match_results
            WHERE counted = 1
              AND match_kind IN ('Ranked', 'Casual', 'Matchmaking')
              AND finish_reason NOT LIKE '%断线%'
              AND LOWER(finish_reason) NOT LIKE '%disconnect%';
            """;
        command.ExecuteNonQuery();
    }

    private static string HashAccount(string account)
    {
        var normalized = account.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string ToDatabaseUtc(DateTime value)
        => (value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture);

    private static string ResolveDefaultDatabasePath()
    {
        var configuredDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
            return Path.Combine(configuredDir, "leader-champions.db");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Data", "leader-champions.db");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Data", "leader-champions.db");
    }

}
