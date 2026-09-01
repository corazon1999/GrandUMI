using System.Collections.Concurrent;
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
    string FinishReason,
    IReadOnlyList<string>? Player0StartingHand = null,
    IReadOnlyList<string>? Player1StartingHand = null);

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
    string FilterTier,
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
    int FirstWins,
    int FirstLosses,
    double? FirstWinRate,
    int SecondGames,
    int SecondWins,
    int SecondLosses,
    double? SecondWinRate,
    bool IsMirror);

public sealed record LeaderMatchupSnapshot(
    string Period,
    string FilterTier,
    DateTime GeneratedAtUtc,
    DateTime? SinceUtc,
    string LeaderNumber,
    IReadOnlyList<LeaderMatchupItem> Items,
    int StartingHandSampleGames,
    IReadOnlyList<LeaderStartingHandItem> StartingHandItems);

public sealed record LeaderStartingHandItem(
    string CardNumber,
    int Games,
    double Percentage);

public sealed record LeaderMatchupMatrixRow(
    string LeaderNumber,
    IReadOnlyList<LeaderMatchupItem> Items);

public sealed record LeaderMatchupMatrixSnapshot(
    string Period,
    string FilterTier,
    DateTime GeneratedAtUtc,
    DateTime? SinceUtc,
    IReadOnlyList<LeaderMatchupMatrixRow> Rows);

public sealed record PlayerLeaderStatsItem(
    string LeaderNumber,
    int Games,
    int Wins,
    int Losses,
    double WinRate,
    double UsageRate,
    int FirstGames,
    double? FirstWinRate,
    int SecondGames,
    double? SecondWinRate);

public sealed record PlayerFavoriteLeader(
    string LeaderNumber,
    int Games,
    int Wins);

public sealed record PlayerStatsTrendPoint(
    string Label,
    int Games,
    int Wins,
    double? WinRate);

public sealed record PlayerProfileStatsSnapshot(
    string Period,
    DateTime GeneratedAtUtc,
    DateTime? SinceUtc,
    int Games,
    int Wins,
    int Losses,
    double WinRate,
    int FirstGames,
    double? FirstWinRate,
    int SecondGames,
    double? SecondWinRate,
    IReadOnlyList<PlayerLeaderStatsItem> TopLeaders,
    IReadOnlyList<PlayerStatsTrendPoint> Trend);

public sealed record DailyMatchCountPoint(string Date, int Count);

/// <summary>
/// Leader 排行榜的逐局事实存储。以 match_id 幂等写入，榜单按时间窗口即时聚合。
/// </summary>
public sealed class LeaderStatsStore : IDisposable
{
    public const int MinimumRankedGames = 20;
    public const string HundredGameFilterTier = "100";
    public const string ThreeHundredGameFilterTier = "300";
    public const string FiveHundredGameFilterTier = "500";
    public const string ThousandGameFilterTier = "1000";
    public const string ThreeThousandGameFilterTier = "3000";
    public const string AllFilterTier = "all";
    public const string LegacyRelaxedFilterTier = "relaxed";
    public const string LegacyStandardFilterTier = "standard";
    public const int MinimumCountedTurn = 8;
    public const int MatchupLeaderboardLimit = 20;
    public const int MatchupMatrixLeaderLimit = 20;
    public const int StartingHandCardLimit = 10;
    public const int StatsVersion = 2;

    private static readonly ConcurrentDictionary<string, object> InitializationGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly object _lock = new();
    private readonly string _databasePath;
    private readonly string _leaderboardDatabasePath;
    private readonly string _writeConnectionString;
    private readonly string _leaderboardConnectionString;
    private readonly Dictionary<string, CachedLeaderboard> _leaderboardCache = new(StringComparer.Ordinal);
    private SqliteConnection? _walAnchorConnection;
    private bool _initialized;
    private bool _disposed;

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
    public bool WalAnchorActive
    {
        get
        {
            lock (_lock) return _walAnchorConnection is not null;
        }
    }

    public void Initialize(bool keepWalAnchor = false)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                if (keepWalAnchor) EnsureWalAnchorLocked();
                return;
            }

            // 同一进程可能为同一路径构造多个 Store（测试服读写分离、回填与测试均会发生）。
            // SQLitePCL 在多个连接同时为一份空库协商 journal_mode 时并不可靠，先按绝对路径串行建库；
            // 跨进程生产写入另由 SingleWriterLease 约束，事务仍负责崩溃恢复与最终原子性。
            lock (InitializationGates.GetOrAdd(_databasePath, static _ => new object()))
            {
                var parent = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

                using var connection = OpenWriteConnection();
                ConfigureConnection(connection);
                var databaseVersion = ReadDatabaseVersion(connection);
                if (databaseVersion > StatsVersion)
                {
                    throw new InvalidOperationException(
                        $"Leader 统计数据库版本 {databaseVersion} 高于当前程序支持的 {StatsVersion}，拒绝以旧程序写入。");
                }

                // 立即事务让建表、历史资格重算、兼容触发器与版本标记一次提交。
                // 进程在任意一步退出都会整体回滚；跨连接写入由 SQLite 写锁串行化。
                using var transaction = connection.BeginTransaction(deferred: false);
                CreateSchema(connection, transaction);
                ReclassifyLegacyMatches(connection, transaction);
                InstallLegacyWriterCompatibilityTrigger(connection, transaction);
                SetDatabaseVersion(connection, transaction);
                transaction.Commit();
                _initialized = true;
            }

            // 测试服以只读 mount 直接读取正式 WAL 数据库。SQLite 即使使用只读连接，
            // 在最后一个正式连接退出并删除 -wal/-shm 后仍需要重新创建侧车，因只读 mount
            // 会得到 EROFS。正式单写者显式保留此连接，让侧车与进程寿命一致；初始化已经
            // 成功时锚点失败仍向上抛出，调用方可重试同一 Store，但绝不能静默无锚运行。
            if (keepWalAnchor) EnsureWalAnchorLocked();
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
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
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
            var inserted = command.ExecuteNonQuery() == 1;
            if (inserted && counted)
            {
                RecordStartingHandCards(connection, transaction, result.MatchId, 0, result.Player0StartingHand);
                RecordStartingHandCards(connection, transaction, result.MatchId, 1, result.Player1StartingHand);
            }
            transaction.Commit();
            if (inserted) _leaderboardCache.Clear();
            return inserted;
        }
    }

    /// <summary>
    /// 按 UTC+8 自然日统计已完成的真人对局；不包含机器人局和同账号测试局。
    /// 这是独立的运营场次口径，仍包含好友与房间码对局，不参与公开 Leader 榜资格判断。
    /// </summary>
    public IReadOnlyList<DailyMatchCountPoint> GetRecentDailyMatchCounts(int days, DateTime? nowUtc = null)
    {
        if (days is < 1 or > 45) throw new ArgumentOutOfRangeException(nameof(days));
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var localToday = now.AddHours(8).Date;
        var firstLocalDate = localToday.AddDays(-(days - 1));
        var firstUtc = DateTime.SpecifyKind(firstLocalDate.AddHours(-8), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(localToday.AddDays(1).AddHours(-8), DateTimeKind.Utc);

        lock (_lock)
        {
            Initialize();
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("对局统计数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT date(ended_at_utc, '+8 hours') AS local_date, COUNT(*)
                FROM match_results
                WHERE ended_at_utc >= $firstUtc
                  AND ended_at_utc < $endUtc
                  AND match_kind <> 'Bot'
                  AND player0_key <> player1_key
                GROUP BY local_date
                ORDER BY local_date;
                """;
            command.Parameters.AddWithValue("$firstUtc", ToDatabaseUtc(firstUtc));
            command.Parameters.AddWithValue("$endUtc", ToDatabaseUtc(endUtc));
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1);

            return Enumerable.Range(0, days)
                .Select(offset => firstLocalDate.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Select(date => new DailyMatchCountPoint(date, counts.GetValueOrDefault(date)))
                .ToArray();
        }
    }

    public LeaderLeaderboardSnapshot GetLeaderboard(
        string? requestedPeriod,
        DateTime? nowUtc = null,
        string? requestedFilterTier = null)
    {
        var period = NormalizePeriod(requestedPeriod);
        var filterTier = NormalizeFilterTier(requestedFilterTier, period);
        var cacheKey = $"{period}:{filterTier}";
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
            if (nowUtc is null
                && _leaderboardCache.TryGetValue(cacheKey, out var cached)
                && generatedAtUtc - cached.CreatedAtUtc < TimeSpan.FromSeconds(15))
                return cached.Snapshot;
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();

            var totalMatches = ReadTotalMatches(connection, sinceUtc);
            var minimumLeaderboardGames = filterTier switch
            {
                HundredGameFilterTier => 100,
                ThreeHundredGameFilterTier => 300,
                FiveHundredGameFilterTier => 500,
                ThousandGameFilterTier => 1000,
                ThreeThousandGameFilterTier => 3000,
                _ => 0,
            };
            var rows = ReadLeaderboardRows(connection, sinceUtc)
                .Where(x => x.Games >= minimumLeaderboardGames);
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

            var snapshot = new LeaderLeaderboardSnapshot(
                period,
                filterTier,
                generatedAtUtc,
                sinceUtc,
                totalMatches,
                MinimumRankedGames,
                items);
            if (nowUtc is null) _leaderboardCache[cacheKey] = new CachedLeaderboard(generatedAtUtc, snapshot);
            return snapshot;
        }
    }

    /// <summary>统计指定 Leader 对阵当前周期排行榜前二十名的表现，以及起手留牌使用率。</summary>
    public LeaderMatchupSnapshot GetMatchups(
        string leaderNumber,
        string? requestedPeriod,
        DateTime? nowUtc = null,
        string? requestedFilterTier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderNumber);
        var normalizedLeader = leaderNumber.Trim();
        var leaderboard = GetLeaderboard(requestedPeriod, nowUtc, requestedFilterTier);
        var topLeaders = leaderboard.Items
            .Where(x => x.Rank is not null)
            .Take(MatchupLeaderboardLimit)
            .ToArray();

        lock (_lock)
        {
            Initialize();
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            var rows = ReadMatchupRows(connection, normalizedLeader, leaderboard.SinceUtc);
            var mirror = ReadMirrorRow(connection, normalizedLeader, leaderboard.SinceUtc);
            var startingHand = ReadStartingHandStats(connection, normalizedLeader, leaderboard.SinceUtc);
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
                        mirror.FirstWins,
                        mirror.Games - mirror.FirstWins,
                        mirror.Games == 0 ? null : mirror.FirstWins / (double)mirror.Games,
                        mirror.Games,
                        mirror.SecondWins,
                        mirror.Games - mirror.SecondWins,
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
                    row.FirstWins,
                    row.FirstGames - row.FirstWins,
                    row.FirstGames == 0 ? null : row.FirstWins / (double)row.FirstGames,
                    row.SecondGames,
                    row.SecondWins,
                    row.SecondGames - row.SecondWins,
                    row.SecondGames == 0 ? null : row.SecondWins / (double)row.SecondGames,
                    false);
            }).ToArray();

            return new LeaderMatchupSnapshot(
                leaderboard.Period,
                leaderboard.FilterTier,
                leaderboard.GeneratedAtUtc,
                leaderboard.SinceUtc,
                normalizedLeader,
                items,
                startingHand.SampleGames,
                startingHand.Items);
        }
    }

    /// <summary>按综合胜率降序统计榜前二十 Leader 的完整对阵矩阵。</summary>
    public LeaderMatchupMatrixSnapshot GetMatchupMatrix(
        string? requestedPeriod,
        DateTime? nowUtc = null,
        string? requestedFilterTier = null)
    {
        var leaderboard = GetLeaderboard(requestedPeriod, nowUtc, requestedFilterTier);
        var leaders = leaderboard.Items
            .Where(x => x.Rank is not null)
            .Take(MatchupMatrixLeaderLimit)
            .ToArray();

        lock (_lock)
        {
            Initialize();
            if (!File.Exists(_leaderboardDatabasePath))
                throw new FileNotFoundException("排行榜数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            var rows = leaders.Select(leader =>
            {
                var matchupRows = ReadMatchupRows(connection, leader.LeaderNumber, leaderboard.SinceUtc);
                var mirror = ReadMirrorRow(connection, leader.LeaderNumber, leaderboard.SinceUtc);
                var items = leaders.Select(opponent =>
                {
                    var rank = opponent.Rank!.Value;
                    if (string.Equals(opponent.LeaderNumber, leader.LeaderNumber, StringComparison.Ordinal))
                    {
                        return new LeaderMatchupItem(
                            rank,
                            opponent.LeaderNumber,
                            mirror.Games,
                            null,
                            null,
                            null,
                            mirror.Games,
                            mirror.FirstWins,
                            mirror.Games - mirror.FirstWins,
                            mirror.Games == 0 ? null : mirror.FirstWins / (double)mirror.Games,
                            mirror.Games,
                            mirror.SecondWins,
                            mirror.Games - mirror.SecondWins,
                            mirror.Games == 0 ? null : mirror.SecondWins / (double)mirror.Games,
                            true);
                    }

                    if (!matchupRows.TryGetValue(opponent.LeaderNumber, out var matchup))
                        matchup = new MatchupAggregateRow(opponent.LeaderNumber, 0, 0, 0, 0, 0, 0);

                    return new LeaderMatchupItem(
                        rank,
                        opponent.LeaderNumber,
                        matchup.Games,
                        matchup.Wins,
                        matchup.Games - matchup.Wins,
                        matchup.Games == 0 ? null : matchup.Wins / (double)matchup.Games,
                        matchup.FirstGames,
                        matchup.FirstWins,
                        matchup.FirstGames - matchup.FirstWins,
                        matchup.FirstGames == 0 ? null : matchup.FirstWins / (double)matchup.FirstGames,
                        matchup.SecondGames,
                        matchup.SecondWins,
                        matchup.SecondGames - matchup.SecondWins,
                        matchup.SecondGames == 0 ? null : matchup.SecondWins / (double)matchup.SecondGames,
                        false);
                }).ToArray();

                return new LeaderMatchupMatrixRow(leader.LeaderNumber, items);
            }).ToArray();

            return new LeaderMatchupMatrixSnapshot(
                leaderboard.Period,
                leaderboard.FilterTier,
                leaderboard.GeneratedAtUtc,
                leaderboard.SinceUtc,
                rows);
        }
    }

    /// <summary>按当前登录账号聚合个人战绩；账号只在服务端哈希后参与查询。</summary>
    public PlayerProfileStatsSnapshot GetPlayerProfile(string account, string? requestedPeriod, DateTime? nowUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
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
                throw new FileNotFoundException("个人统计数据源不存在。", _leaderboardDatabasePath);

            using var connection = OpenLeaderboardConnection();
            var appearances = ReadPlayerAppearances(connection, HashAccount(account), sinceUtc);
            var games = appearances.Count;
            var wins = appearances.Count(x => x.Won);
            var firstGames = appearances.Count(x => x.WentFirst);
            var firstWins = appearances.Count(x => x.WentFirst && x.Won);
            var secondGames = games - firstGames;
            var secondWins = wins - firstWins;

            var topLeaders = appearances
                .GroupBy(x => x.LeaderNumber, StringComparer.Ordinal)
                .Select(group =>
                {
                    var leaderGames = group.Count();
                    var leaderWins = group.Count(x => x.Won);
                    var leaderFirstGames = group.Count(x => x.WentFirst);
                    var leaderFirstWins = group.Count(x => x.WentFirst && x.Won);
                    var leaderSecondGames = leaderGames - leaderFirstGames;
                    var leaderSecondWins = leaderWins - leaderFirstWins;
                    return new PlayerLeaderStatsItem(
                        group.Key,
                        leaderGames,
                        leaderWins,
                        leaderGames - leaderWins,
                        leaderWins / (double)leaderGames,
                        games == 0 ? 0 : leaderGames / (double)games,
                        leaderFirstGames,
                        leaderFirstGames == 0 ? null : leaderFirstWins / (double)leaderFirstGames,
                        leaderSecondGames,
                        leaderSecondGames == 0 ? null : leaderSecondWins / (double)leaderSecondGames);
                })
                .OrderByDescending(x => x.Games)
                .ThenByDescending(x => x.WinRate)
                .ThenBy(x => x.LeaderNumber, StringComparer.Ordinal)
                .ToArray();

            return new PlayerProfileStatsSnapshot(
                period,
                generatedAtUtc,
                sinceUtc,
                games,
                wins,
                games - wins,
                games == 0 ? 0 : wins / (double)games,
                firstGames,
                firstGames == 0 ? null : firstWins / (double)firstGames,
                secondGames,
                secondGames == 0 ? null : secondWins / (double)secondGames,
                topLeaders,
                BuildPlayerTrend(appearances, period, generatedAtUtc));
        }
    }

    /// <summary>按账号哈希批量读取最常使用的有效对局 Leader，供公开排位榜展示。</summary>
    public IReadOnlyDictionary<string, PlayerFavoriteLeader> GetFavoriteLeaders(IEnumerable<string> playerKeys)
    {
        var keys = playerKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0) return new Dictionary<string, PlayerFavoriteLeader>(StringComparer.Ordinal);

        lock (_lock)
        {
            Initialize();
            if (!File.Exists(_leaderboardDatabasePath))
                return new Dictionary<string, PlayerFavoriteLeader>(StringComparer.Ordinal);

            using var connection = OpenLeaderboardConnection();
            using var command = connection.CreateCommand();
            var parameters = keys.Select((_, index) => $"$key{index}").ToArray();
            command.CommandText = $"""
                WITH appearances AS (
                    SELECT player0_key AS player_key, player0_leader AS leader_number,
                           CASE WHEN winner_index = 0 THEN 1 ELSE 0 END AS won
                    FROM match_results
                    WHERE counted = 1
                      AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                      AND player0_key IN ({string.Join(",", parameters)})
                    UNION ALL
                    SELECT player1_key AS player_key, player1_leader AS leader_number,
                           CASE WHEN winner_index = 1 THEN 1 ELSE 0 END AS won
                    FROM match_results
                    WHERE counted = 1
                      AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                      AND player1_key IN ({string.Join(",", parameters)})
                ), grouped AS (
                    SELECT player_key, leader_number, COUNT(*) AS games, SUM(won) AS wins,
                           ROW_NUMBER() OVER (
                               PARTITION BY player_key
                               ORDER BY COUNT(*) DESC, SUM(won) * 1.0 / COUNT(*) DESC, leader_number ASC
                           ) AS leader_rank
                    FROM appearances
                    GROUP BY player_key, leader_number
                )
                SELECT player_key, leader_number, games, wins
                FROM grouped
                WHERE leader_rank = 1;
                """;
            for (var index = 0; index < keys.Length; index++)
                command.Parameters.AddWithValue(parameters[index], keys[index]);

            var result = new Dictionary<string, PlayerFavoriteLeader>(StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = new PlayerFavoriteLeader(reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
            return result;
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

    private void EnsureWalAnchorLocked()
    {
        if (_walAnchorConnection is not null) return;

        SqliteConnection? anchor = null;
        try
        {
            anchor = OpenWriteConnection();
            ConfigureConnection(anchor);
            using (var command = anchor.CreateCommand())
            {
                // 实际读取一次 schema，确保 SQLite 已进入 WAL 读取路径并物化 -wal/-shm；
                // 只打开句柄但从未访问页面，不能证明侧车已经存在。
                command.CommandText = "SELECT 1 FROM sqlite_schema LIMIT 1;";
                command.ExecuteScalar();
            }
            if (!File.Exists(_databasePath + "-wal") || !File.Exists(_databasePath + "-shm"))
                throw new InvalidOperationException("Leader 统计 WAL 生命周期锚点未能建立侧车文件。");
            _walAnchorConnection = anchor;
            anchor = null;
        }
        finally
        {
            anchor?.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LeaderStatsStore));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            var anchor = _walAnchorConnection;
            _walAnchorConnection = null;
            _leaderboardCache.Clear();
            anchor?.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        // journal_mode 会返回结果集，不能与其他 PRAGMA 拼成 ExecuteNonQuery 批处理；
        // Microsoft.Data.Sqlite 在多个初始化连接并发消费该批处理时可能错误枚举语句。
        command.CommandText = "PRAGMA journal_mode;";
        var currentMode = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (string.Equals(currentMode, "wal", StringComparison.OrdinalIgnoreCase)) return;

        command.CommandText = "PRAGMA journal_mode = WAL;";
        var configuredMode = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(configuredMode, "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Leader 统计数据库无法启用 WAL，当前模式：{configuredMode ?? "未知"}。");
    }

    private static int ReadDatabaseVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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

            CREATE TABLE IF NOT EXISTS match_starting_hand_cards (
                match_id            TEXT NOT NULL,
                player_index        INTEGER NOT NULL,
                card_number         TEXT NOT NULL,
                PRIMARY KEY (match_id, player_index, card_number)
            );

            CREATE INDEX IF NOT EXISTS ix_match_starting_hand_cards_match
                ON match_starting_hand_cards(match_id, player_index);

            CREATE TABLE IF NOT EXISTS leader_stats_migrations (
                version             INTEGER PRIMARY KEY,
                applied_at_utc      TEXT NOT NULL,
                description         TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void ReclassifyLegacyMatches(SqliteConnection connection, SqliteTransaction transaction)
    {
        var countedSql = BuildCountedEligibilitySql();
        var excludeReasonSql = BuildExcludeReasonSql();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE match_results
            SET counted = {countedSql},
                exclude_reason = {excludeReasonSql},
                stats_version = {StatsVersion}
            WHERE stats_version < {StatsVersion};
            """;
        command.ExecuteNonQuery();
    }

    private static void InstallLegacyWriterCompatibilityTrigger(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var countedSql = BuildCountedEligibilitySql();
        var excludeReasonSql = BuildExcludeReasonSql();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DROP TRIGGER IF EXISTS trg_match_results_upgrade_legacy_insert;
            CREATE TRIGGER trg_match_results_upgrade_legacy_insert
            AFTER INSERT ON match_results
            WHEN NEW.stats_version < {StatsVersion}
            BEGIN
                UPDATE match_results
                SET counted = {countedSql},
                    exclude_reason = {excludeReasonSql},
                    stats_version = {StatsVersion}
                WHERE match_id = NEW.match_id;
            END;
            """;
        command.ExecuteNonQuery();
    }

    private static void SetDatabaseVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT OR IGNORE INTO leader_stats_migrations (version, applied_at_utc, description)
            VALUES ({StatsVersion}, $appliedAtUtc, 'public_match_only');
            PRAGMA user_version = {StatsVersion};
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", ToDatabaseUtc(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    private static string BuildCountedEligibilitySql()
        => $"""
            CASE
                WHEN match_kind IS NULL OR match_kind NOT IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql}) THEN 0
                WHEN winner_index IS NULL OR winner_index NOT IN (0, 1) THEN 0
                WHEN finish_reason LIKE '%断线%' OR LOWER(finish_reason) LIKE '%disconnect%' THEN 0
                WHEN turn_count < {MinimumCountedTurn} THEN 0
                WHEN player0_key = player1_key THEN 0
                ELSE 1
            END
            """;

    private static string BuildExcludeReasonSql()
        => $"""
            CASE
                WHEN match_kind = 'Bot' THEN 'bot'
                WHEN match_kind IN ('Friendly', 'RoomCode') THEN 'private_match'
                WHEN match_kind IS NULL OR match_kind NOT IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql}) THEN 'unsupported_match_kind'
                WHEN winner_index IS NULL OR winner_index NOT IN (0, 1) THEN 'no_winner'
                WHEN finish_reason LIKE '%断线%' OR LOWER(finish_reason) LIKE '%disconnect%' THEN 'disconnect'
                WHEN turn_count < {MinimumCountedTurn} THEN 'too_short'
                WHEN player0_key = player1_key THEN 'same_account'
                ELSE NULL
            END
            """;

    private static (bool Counted, string? ExcludeReason) EvaluateEligibility(LeaderMatchResult result)
    {
        if (!LeaderStatsEligibilityPolicy.IsPublicMatch(result.MatchKind))
            return (false, LeaderStatsEligibilityPolicy.ExcludedMatchKindReason(result.MatchKind));
        if (result.WinnerIndex is not (0 or 1)) return (false, "no_winner");
        if (IsDisconnectFinish(result.FinishReason)) return (false, "disconnect");
        if (result.TurnCount < MinimumCountedTurn) return (false, "too_short");
        if (string.Equals(
                result.Player0Account.Trim(),
                result.Player1Account.Trim(),
                StringComparison.OrdinalIgnoreCase))
            return (false, "same_account");
        return (true, null);
    }

    private static bool IsDisconnectFinish(string? finishReason)
        => !string.IsNullOrWhiteSpace(finishReason)
           && (finishReason.Contains("断线", StringComparison.Ordinal)
               || finishReason.Contains("disconnect", StringComparison.OrdinalIgnoreCase));

    private static int ReadTotalMatches(SqliteConnection connection, DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM match_results
            WHERE counted = 1
              AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
              AND finish_reason NOT LIKE '%断线%'
              AND LOWER(finish_reason) NOT LIKE '%disconnect%'
              AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc);
            """;
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<AggregateRow> ReadLeaderboardRows(SqliteConnection connection, DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH filtered AS (
                SELECT player0_leader, player1_leader, winner_index, first_player_index
                FROM match_results
                WHERE counted = 1
                  AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                  AND finish_reason NOT LIKE '%断线%'
                  AND LOWER(finish_reason) NOT LIKE '%disconnect%'
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

    private static void RecordStartingHandCards(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string matchId,
        int playerIndex,
        IReadOnlyList<string>? cards)
    {
        if (cards is null) return;

        var distinctCards = cards
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .Select(card => card.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctCards.Length == 0) return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO match_starting_hand_cards (match_id, player_index, card_number)
            VALUES ($matchId, $playerIndex, $cardNumber);
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$playerIndex", playerIndex);
        var cardParameter = command.Parameters.Add("$cardNumber", SqliteType.Text);
        foreach (var card in distinctCards)
        {
            cardParameter.Value = card;
            command.ExecuteNonQuery();
        }
    }

    private static StartingHandStats ReadStartingHandStats(
        SqliteConnection connection,
        string leaderNumber,
        DateTime? sinceUtc)
    {
        // 允许测试服暂时读取尚未完成迁移的只读榜单库；此时起手分析显示为等待采样，其他统计照常可用。
        using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'match_starting_hand_cards' LIMIT 1;";
            if (schemaCommand.ExecuteScalar() is null)
                return new StartingHandStats(0, Array.Empty<LeaderStartingHandItem>());
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH leader_matches AS (
                SELECT match_id, 0 AS player_index
                FROM match_results
                WHERE counted = 1
                  AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                  AND finish_reason NOT LIKE '%断线%'
                  AND LOWER(finish_reason) NOT LIKE '%disconnect%'
                  AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
                  AND player0_leader = $leaderNumber
                UNION ALL
                SELECT match_id, 1 AS player_index
                FROM match_results
                WHERE counted = 1
                  AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                  AND finish_reason NOT LIKE '%断线%'
                  AND LOWER(finish_reason) NOT LIKE '%disconnect%'
                  AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
                  AND player1_leader = $leaderNumber
            ), sampled_matches AS (
                SELECT DISTINCT leader_matches.match_id, leader_matches.player_index
                FROM leader_matches
                INNER JOIN match_starting_hand_cards
                    ON match_starting_hand_cards.match_id = leader_matches.match_id
                   AND match_starting_hand_cards.player_index = leader_matches.player_index
            ), card_counts AS (
                SELECT match_starting_hand_cards.card_number, COUNT(*) AS games
                FROM sampled_matches
                INNER JOIN match_starting_hand_cards
                    ON match_starting_hand_cards.match_id = sampled_matches.match_id
                   AND match_starting_hand_cards.player_index = sampled_matches.player_index
                GROUP BY match_starting_hand_cards.card_number
            )
            SELECT
                (SELECT COUNT(*) FROM sampled_matches) AS sample_games,
                card_number,
                games
            FROM card_counts
            ORDER BY games DESC, card_number ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$leaderNumber", leaderNumber);
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));
        command.Parameters.AddWithValue("$limit", StartingHandCardLimit);

        var items = new List<LeaderStartingHandItem>();
        var sampleGames = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sampleGames = reader.GetInt32(0);
            var games = reader.GetInt32(2);
            items.Add(new LeaderStartingHandItem(
                reader.GetString(1),
                games,
                sampleGames == 0 ? 0 : games / (double)sampleGames));
        }
        return new StartingHandStats(sampleGames, items);
    }

    private static Dictionary<string, MatchupAggregateRow> ReadMatchupRows(
        SqliteConnection connection,
        string leaderNumber,
        DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH filtered AS (
                SELECT player0_leader, player1_leader, winner_index, first_player_index
                FROM match_results
                WHERE counted = 1
                  AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
                  AND finish_reason NOT LIKE '%断线%'
                  AND LOWER(finish_reason) NOT LIKE '%disconnect%'
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
        command.CommandText = $"""
            SELECT
                COUNT(*) AS games,
                COALESCE(SUM(CASE WHEN winner_index = first_player_index THEN 1 ELSE 0 END), 0) AS first_wins,
                COALESCE(SUM(CASE WHEN winner_index <> first_player_index THEN 1 ELSE 0 END), 0) AS second_wins
            FROM match_results
            WHERE counted = 1
              AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
              AND finish_reason NOT LIKE '%断线%'
              AND LOWER(finish_reason) NOT LIKE '%disconnect%'
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

    private static List<PlayerAppearanceRow> ReadPlayerAppearances(
        SqliteConnection connection,
        string playerKey,
        DateTime? sinceUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ended_at_utc,
                CASE WHEN player0_key = $playerKey THEN player0_leader ELSE player1_leader END AS leader_number,
                CASE
                    WHEN player0_key = $playerKey AND winner_index = 0 THEN 1
                    WHEN player1_key = $playerKey AND winner_index = 1 THEN 1
                    ELSE 0
                END AS won,
                CASE
                    WHEN player0_key = $playerKey AND first_player_index = 0 THEN 1
                    WHEN player1_key = $playerKey AND first_player_index = 1 THEN 1
                    ELSE 0
                END AS went_first
            FROM match_results
            WHERE counted = 1
              AND match_kind IN ({LeaderStatsEligibilityPolicy.PublicMatchKindsSql})
              AND finish_reason NOT LIKE '%断线%'
              AND LOWER(finish_reason) NOT LIKE '%disconnect%'
              AND (player0_key = $playerKey OR player1_key = $playerKey)
              AND ($sinceUtc IS NULL OR ended_at_utc >= $sinceUtc)
            ORDER BY ended_at_utc;
            """;
        command.Parameters.AddWithValue("$playerKey", playerKey);
        command.Parameters.AddWithValue("$sinceUtc", sinceUtc is null ? DBNull.Value : ToDatabaseUtc(sinceUtc.Value));

        var rows = new List<PlayerAppearanceRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var endedAtUtc = DateTime.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind).ToUniversalTime();
            rows.Add(new PlayerAppearanceRow(
                endedAtUtc,
                reader.GetString(1),
                reader.GetInt32(2) == 1,
                reader.GetInt32(3) == 1));
        }
        return rows;
    }

    private static IReadOnlyList<PlayerStatsTrendPoint> BuildPlayerTrend(
        IReadOnlyList<PlayerAppearanceRow> appearances,
        string period,
        DateTime generatedAtUtc)
    {
        if (period == "all")
        {
            return appearances
                .GroupBy(x => new DateTime(x.EndedAtUtc.Year, x.EndedAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(group => group.Key)
                .TakeLast(12)
                .Select(group => BuildTrendPoint(group.Key.ToString("yyyy/MM", CultureInfo.InvariantCulture), group))
                .ToArray();
        }

        var bucketDays = period == "7d" ? 1 : 3;
        var bucketCount = period == "7d" ? 7 : 10;
        var startUtc = generatedAtUtc.Date.AddDays(-(bucketDays * bucketCount - 1));
        var buckets = Enumerable.Range(0, bucketCount)
            .Select(index => new List<PlayerAppearanceRow>())
            .ToArray();
        foreach (var appearance in appearances)
        {
            var bucket = (appearance.EndedAtUtc.Date - startUtc).Days / bucketDays;
            if (bucket >= 0 && bucket < bucketCount) buckets[bucket].Add(appearance);
        }

        return buckets.Select((bucket, index) =>
        {
            var bucketStart = startUtc.AddDays(index * bucketDays);
            return BuildTrendPoint(bucketStart.ToString("MM/dd", CultureInfo.InvariantCulture), bucket);
        }).ToArray();
    }

    private static PlayerStatsTrendPoint BuildTrendPoint(
        string label,
        IEnumerable<PlayerAppearanceRow> source)
    {
        var rows = source.ToArray();
        var games = rows.Length;
        var wins = rows.Count(x => x.Won);
        return new PlayerStatsTrendPoint(label, games, wins, games == 0 ? null : wins / (double)games);
    }

    private static string NormalizePeriod(string? period)
        => period?.Trim().ToLowerInvariant() switch
        {
            "7d" => "7d",
            "30d" => "30d",
            "all" => "all",
            _ => "7d",
        };

    /// <summary>
    /// 将新客户端的固定场次档位规范化。旧版 relaxed / standard 与缺省值仍按旧周期口径映射，
    /// 使旧客户端升级服务端后保持原有筛选结果；新版六档一旦选定便不再受周期影响。
    /// </summary>
    public static string NormalizeFilterTier(string? filterTier, string? requestedPeriod = null)
    {
        var period = NormalizePeriod(requestedPeriod);
        return filterTier?.Trim().ToLowerInvariant() switch
        {
            HundredGameFilterTier => HundredGameFilterTier,
            ThreeHundredGameFilterTier => ThreeHundredGameFilterTier,
            FiveHundredGameFilterTier => FiveHundredGameFilterTier,
            ThousandGameFilterTier => ThousandGameFilterTier,
            ThreeThousandGameFilterTier => ThreeThousandGameFilterTier,
            AllFilterTier => AllFilterTier,
            LegacyRelaxedFilterTier => period switch
            {
                "30d" => ThreeHundredGameFilterTier,
                "all" => AllFilterTier,
                _ => HundredGameFilterTier,
            },
            _ => period switch
            {
                "30d" => ThreeThousandGameFilterTier,
                "all" => AllFilterTier,
                _ => FiveHundredGameFilterTier,
            },
        };
    }

    /// <summary>旧客户端依赖回包原样携带 relaxed / standard 来关联请求，其余客户端回显规范化后的固定档位。</summary>
    public static string NormalizeFilterTierForResponse(string? filterTier, string? requestedPeriod = null)
    {
        var legacyTier = filterTier?.Trim().ToLowerInvariant();
        return legacyTier is LegacyRelaxedFilterTier or LegacyStandardFilterTier
            ? legacyTier
            : NormalizeFilterTier(filterTier, requestedPeriod);
    }

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

    private sealed record StartingHandStats(int SampleGames, IReadOnlyList<LeaderStartingHandItem> Items);

    private sealed record PlayerAppearanceRow(
        DateTime EndedAtUtc,
        string LeaderNumber,
        bool Won,
        bool WentFirst);

    private sealed record CachedLeaderboard(DateTime CreatedAtUtc, LeaderLeaderboardSnapshot Snapshot);
}
