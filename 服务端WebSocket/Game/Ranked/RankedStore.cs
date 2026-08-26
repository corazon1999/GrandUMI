using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using GrandUMI.Game.Stats;

namespace GrandUMI.Game.Ranked;

public enum RankedMode
{
    Standard,
    Wild,
}

public static class RankedModeWire
{
    public const string Standard = "standard";
    public const string Wild = "wild";

    public static RankedMode Parse(string? value)
        => string.Equals(value, Wild, StringComparison.OrdinalIgnoreCase) ? RankedMode.Wild : RankedMode.Standard;

    public static string Value(RankedMode mode) => mode == RankedMode.Wild ? Wild : Standard;
}

public sealed record RankProfileSnapshot(
    string SeasonId,
    DateTime SeasonStartsAtUtc,
    DateTime SeasonEndsAtUtc,
    int PlacementGames,
    int PlacementRequired,
    int RankPoints,
    string? Faction,
    string Tier,
    int? Division,
    int Games,
    int Wins,
    int Losses,
    int HighestRankPoints,
    IReadOnlyList<string> ChampionLeaderNumbers);

public sealed record RankLeaderboardItem(
    int Rank,
    int FactionRank,
    string DisplayName,
    int RankPoints,
    string Faction,
    string Tier,
    int? Division,
    int Games,
    int Wins,
    double WinRate,
    string? FavoriteLeader,
    IReadOnlyList<string> ChampionLeaderNumbers,
    bool IsCurrentPlayer);

public sealed record FactionStanding(
    int Rank,
    string Faction,
    long TotalRankPoints,
    int PlayerCount,
    int Games,
    int Wins);

public sealed record RankSnapshot(
    RankProfileSnapshot Profile,
    IReadOnlyList<RankLeaderboardItem> Leaderboard,
    IReadOnlyList<FactionStanding> FactionStandings,
    long SnapshotVersion,
    DateTime GeneratedAtUtc);

public sealed class RankLeaderboardUnavailableException : Exception
{
    public RankLeaderboardUnavailableException(string message) : base(message)
    {
    }
}

public sealed record RankPlayerSettlement(
    string Account,
    int RankPointsBefore,
    int RankPointsAfter,
    int RankPointDelta,
    int BaseRankPointDelta,
    int StreakAdjustment,
    int WinStreakEndedBounty,
    int EndedWinStreak,
    int RankDifference,
    int RankDifferenceAdjustment,
    int RankProtectionAdjustment,
    int ResultStreak,
    bool Won,
    bool RankPointFormulaApplied,
    string Faction,
    string Tier,
    int? Division,
    int PlacementGames,
    int PlacementRequired,
    bool PlacementCompleted,
    int WinStreakBefore,
    int WinStreak);

public sealed record RankedMatchSettlement(
    string MatchId,
    RankPlayerSettlement Player0,
    RankPlayerSettlement Player1);

public static class RankWire
{
    public static object Profile(RankProfileSnapshot value) => new
    {
        seasonId = value.SeasonId,
        seasonStartsAtUtc = value.SeasonStartsAtUtc,
        seasonEndsAtUtc = value.SeasonEndsAtUtc,
        placementGames = value.PlacementGames,
        placementRequired = value.PlacementRequired,
        rankPoints = value.RankPoints,
        faction = value.Faction,
        tier = value.Tier,
        division = value.Division,
        games = value.Games,
        wins = value.Wins,
        losses = value.Losses,
        highestRankPoints = value.HighestRankPoints,
        championLeaderNumbers = value.ChampionLeaderNumbers,
    };

    public static object[] Leaderboard(IReadOnlyList<RankLeaderboardItem> values)
        => values.Select(value => (object)new
        {
            rank = value.Rank,
            factionRank = value.FactionRank,
            displayName = value.DisplayName,
            rankPoints = value.RankPoints,
            faction = value.Faction,
            tier = value.Tier,
            division = value.Division,
            games = value.Games,
            wins = value.Wins,
            winRate = value.WinRate,
            favoriteLeader = value.FavoriteLeader,
            championLeaderNumbers = value.ChampionLeaderNumbers,
            isCurrentPlayer = value.IsCurrentPlayer,
        }).ToArray();

    public static object[] FactionStandings(IReadOnlyList<FactionStanding> values)
        => values.Select(value => (object)new
        {
            rank = value.Rank,
            faction = value.Faction,
            totalRankPoints = value.TotalRankPoints,
            playerCount = value.PlayerCount,
            games = value.Games,
            wins = value.Wins,
        }).ToArray();

    public static object Settlement(RankPlayerSettlement value) => new
    {
        account = value.Account,
        rankPointsBefore = value.RankPointsBefore,
        rankPointsAfter = value.RankPointsAfter,
        rankPointDelta = value.RankPointDelta,
        baseRankPointDelta = value.BaseRankPointDelta,
        streakAdjustment = value.StreakAdjustment,
        winStreakEndedBounty = value.WinStreakEndedBounty,
        endedWinStreak = value.EndedWinStreak,
        rankDifference = value.RankDifference,
        rankDifferenceAdjustment = value.RankDifferenceAdjustment,
        rankProtectionAdjustment = value.RankProtectionAdjustment,
        resultStreak = value.ResultStreak,
        won = value.Won,
        rankPointFormulaApplied = value.RankPointFormulaApplied,
        faction = value.Faction,
        tier = value.Tier,
        division = value.Division,
        placementGames = value.PlacementGames,
        placementRequired = value.PlacementRequired,
        placementCompleted = value.PlacementCompleted,
        winStreak = value.WinStreak,
    };
}

/// <summary>
/// 排位赛独立 SQLite。测试服和正式服可能共用玩家资料库，因此排位数据必须通过
/// GRANDUMI_RANKED_DB 按环境隔离，不能写入 players.db。
/// </summary>
public sealed class RankedStore
{
    public const string PirateFaction = "pirate";
    public const string MarineFaction = "marine";
    public const string GovernmentFaction = "government";
    public const int PlacementRequired = 5;
    public const int NewWorldRankPoints = 1500;
    public const int ThreeHundredMillionBountyRankPoints = 3000;
    public const int SixHundredMillionBountyRankPoints = 6000;
    public const int TenBillionBountyRankPoints = 10000;
    public static readonly TimeSpan LeaderboardRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly int[] PermanentBountyProtectionFloors =
    [
        TenBillionBountyRankPoints,
        SixHundredMillionBountyRankPoints,
        ThreeHundredMillionBountyRankPoints,
        NewWorldRankPoints,
    ];
    private const double InitialRating = 1500;
    private const double InitialDeviation = 350;
    private const double InitialVolatility = 0.06;
    private const double GlickoScale = 173.7178;
    private const double Tau = 0.5;
    private static readonly DateTime SeasonAnchorUtc = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SeasonLength = TimeSpan.FromDays(56);
    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly LeaderChampionStore _championStore;
    private readonly LeaderStatsStore _leaderStatsStore;
    private readonly SemaphoreSlim _leaderboardRefreshGate = new(1, 1);
    private PublicLeaderboardSnapshot? _publicLeaderboardSnapshot;
    private string? _lastLeaderboardRefreshError;
    private bool _initialized;

    internal Action? BeforeLeaderboardSnapshotBuildForTesting { get; set; }

    public static RankedStore Default { get; } = new();
    public static RankedStore Wild { get; } = new(ResolveWildDefaultPath());

    public static RankedStore ForMode(RankedMode mode) => mode == RankedMode.Wild ? Wild : Default;

    public RankedStore(
        string? databasePath = null,
        LeaderChampionStore? championStore = null,
        LeaderStatsStore? leaderStatsStore = null)
    {
        _databasePath = Path.GetFullPath(databasePath ?? ResolveDefaultPath());
        _championStore = championStore ?? LeaderChampionStore.Default;
        _leaderStatsStore = leaderStatsStore ?? LeaderStatsStore.Default;
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
    public string? LastLeaderboardRefreshError => Volatile.Read(ref _lastLeaderboardRefreshError);

    public static string ResolveDefaultPath()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_RANKED_DB");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dataDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "ranked.db"));
        return Path.Combine(Path.GetDirectoryName(Persistence.PlayerDataStore.ResolveDefaultPath())!, "ranked.db");
    }

    public static string ResolveWildDefaultPath()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_RANKED_WILD_DB");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dataDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "ranked-wild.db"));
        return Path.Combine(Path.GetDirectoryName(Persistence.PlayerDataStore.ResolveDefaultPath())!, "ranked-wild.db");
    }

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA busy_timeout=5000;

                CREATE TABLE IF NOT EXISTS rank_profiles (
                    season_id          TEXT NOT NULL,
                    account_key        TEXT NOT NULL,
                    display_name       TEXT NOT NULL,
                    rating             REAL NOT NULL,
                    rating_deviation   REAL NOT NULL,
                    volatility         REAL NOT NULL,
                    rank_points        INTEGER NOT NULL,
                    highest_rank_points INTEGER NOT NULL,
                    placement_games    INTEGER NOT NULL,
                    games              INTEGER NOT NULL,
                    wins               INTEGER NOT NULL,
                    losses             INTEGER NOT NULL,
                    updated_at_utc     TEXT NOT NULL,
                    PRIMARY KEY(season_id, account_key)
                );

                CREATE TABLE IF NOT EXISTS rank_factions (
                    account_key        TEXT PRIMARY KEY,
                    faction            TEXT NOT NULL,
                    selected_at_utc    TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ranked_matches (
                    match_id            TEXT PRIMARY KEY,
                    season_id           TEXT NOT NULL,
                    ended_at_utc         TEXT NOT NULL,
                    player0_key          TEXT NOT NULL,
                    player1_key          TEXT NOT NULL,
                    winner_index         INTEGER NOT NULL,
                    player0_rp_delta     INTEGER NOT NULL,
                    player1_rp_delta     INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS rank_rating_events (
                    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    match_id            TEXT NOT NULL,
                    season_id           TEXT NOT NULL,
                    account_key         TEXT NOT NULL,
                    rating_before       REAL NOT NULL,
                    rating_after        REAL NOT NULL,
                    rp_before           INTEGER NOT NULL,
                    rp_after            INTEGER NOT NULL,
                    created_at_utc      TEXT NOT NULL,
                    UNIQUE(match_id, account_key)
                );

                CREATE INDEX IF NOT EXISTS ix_rank_profiles_leaderboard
                    ON rank_profiles(season_id, placement_games, rank_points DESC);
                CREATE INDEX IF NOT EXISTS ix_ranked_matches_season_player0_ended
                    ON ranked_matches(season_id, player0_key, ended_at_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_ranked_matches_season_player1_ended
                    ON ranked_matches(season_id, player1_key, ended_at_utc DESC);

                CREATE TABLE IF NOT EXISTS rank_public_snapshot_state (
                    id                  INTEGER PRIMARY KEY CHECK(id = 1),
                    last_version        INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }

    public RankSnapshot GetSnapshot(string account, string? displayName = null, DateTime? nowUtc = null)
    {
        var observedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        // 冷启动同步补热期间仍可能发生结算；先固定公共版本，再读取个人资料，
        // 确保同一响应里的实时资料不会早于刚生成的公共榜单。
        var publicSnapshot = GetCurrentPublicSnapshot(SeasonAt(observedAtUtc), observedAtUtc);
        var player = ReadRealtimePlayer(account, displayName, observedAtUtc);
        return CombineSnapshot(player, publicSnapshot, account, nowUtc);
    }

    public RankProfileSnapshot GetProfileSnapshot(string account, string? displayName = null, DateTime? nowUtc = null)
    {
        var observedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var player = ReadRealtimePlayer(account, displayName, observedAtUtc);
        return ToSnapshot(player.Profile, player.Season, player.Faction, player.FactionRank, account, nowUtc);
    }

    private RealtimePlayer ReadRealtimePlayer(string account, string? displayName, DateTime observedAtUtc)
    {
        lock (_gate)
        {
            Initialize();
            var season = SeasonAt(observedAtUtc);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var profile = LoadOrCreate(connection, transaction, season, account, displayName ?? account);
            if (!string.Equals(profile.DisplayName, displayName ?? account, StringComparison.Ordinal))
            {
                profile = profile with { DisplayName = displayName ?? account };
                Save(connection, transaction, profile);
            }
            var faction = ReadFaction(connection, transaction, profile.AccountKey);
            var factionRank = FactionRank(connection, season, profile, faction, transaction);
            transaction.Commit();
            return new RealtimePlayer(profile, season, faction, factionRank);
        }
    }

    /// <summary>阵营仅影响称号和阵营排行榜；更换阵营会重置当前赛季的排位进度。</summary>
    public RankSnapshot? SelectFaction(string account, string? displayName, string faction, DateTime? nowUtc = null,
        bool resetRankProgress = false)
    {
        faction = NormalizeFaction(faction) ?? string.Empty;
        if (faction.Length == 0) return null;

        var observedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var season = SeasonAt(observedAtUtc);
        // 先确保当前赛季有可用公共快照，避免阵营已写入却因榜单失败返回整体失败。
        var publicSnapshot = GetCurrentPublicSnapshot(season, observedAtUtc);
        RealtimePlayer player;

        lock (_gate)
        {
            Initialize();
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var profile = LoadOrCreate(connection, transaction, season, account, displayName ?? account);
            var selected = ReadFaction(connection, transaction, profile.AccountKey);
            if (selected is null)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO rank_factions(account_key,faction,selected_at_utc) VALUES($key,$faction,$selected);";
                insert.Parameters.AddWithValue("$key", profile.AccountKey);
                insert.Parameters.AddWithValue("$faction", faction);
                insert.Parameters.AddWithValue("$selected", observedAtUtc.ToString("O", CultureInfo.InvariantCulture));
                insert.ExecuteNonQuery();
                selected = faction;
            }
            else if (!string.Equals(selected, faction, StringComparison.Ordinal))
            {
                // 旧客户端未明确确认时保持原阵营，确保请求不会意外清空进度。
                if (resetRankProgress)
                {
                    profile = ResetRankProgress(profile, observedAtUtc);
                    Save(connection, transaction, profile);
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE rank_factions SET faction=$faction, selected_at_utc=$selected WHERE account_key=$key;";
                    update.Parameters.AddWithValue("$key", profile.AccountKey);
                    update.Parameters.AddWithValue("$faction", faction);
                    update.Parameters.AddWithValue("$selected", observedAtUtc.ToString("O", CultureInfo.InvariantCulture));
                    update.ExecuteNonQuery();
                    selected = faction;
                }
            }
            var factionRank = FactionRank(connection, season, profile, selected, transaction);
            transaction.Commit();
            player = new RealtimePlayer(profile, season, selected, factionRank);
        }

        return CombineSnapshot(player, publicSnapshot, account, nowUtc);
    }

    public double GetMatchRating(string account, string? displayName = null, DateTime? nowUtc = null)
    {
        lock (_gate)
        {
            Initialize();
            var season = SeasonAt(nowUtc ?? DateTime.UtcNow);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var profile = LoadOrCreate(connection, transaction, season, account, displayName ?? account);
            transaction.Commit();
            return profile.Rating;
        }
    }

    public RankedMatchSettlement? RecordMatch(
        string matchId,
        DateTime endedAtUtc,
        string player0Account,
        string player0Name,
        string player1Account,
        string player1Name,
        int winnerIndex)
    {
        if (winnerIndex is not (0 or 1)) return null;
        lock (_gate)
        {
            Initialize();
            var season = SeasonAt(endedAtUtc);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (MatchExists(connection, transaction, matchId)) return null;

            var before0 = LoadOrCreate(connection, transaction, season, player0Account, player0Name);
            var before1 = LoadOrCreate(connection, transaction, season, player1Account, player1Name);
            var score0 = winnerIndex == 0 ? 1d : 0d;
            var score1 = 1d - score0;
            var afterRating0 = UpdateRating(before0, before1, score0);
            var afterRating1 = UpdateRating(before1, before0, score1);
            var winStreakBefore0 = CurrentWinStreak(connection, transaction, season.Id, before0.AccountKey);
            var winStreakBefore1 = CurrentWinStreak(connection, transaction, season.Id, before1.AccountKey);
            var lossStreakBefore0 = CurrentLossStreak(connection, transaction, season.Id, before0.AccountKey);
            var lossStreakBefore1 = CurrentLossStreak(connection, transaction, season.Id, before1.AccountKey);
            var resultStreak0 = (score0 > 0.5 ? winStreakBefore0 : lossStreakBefore0) + 1;
            var resultStreak1 = (score1 > 0.5 ? winStreakBefore1 : lossStreakBefore1) + 1;
            var winStreakEndedBounty0 = winnerIndex == 0
                ? CalculateWinStreakEndedBounty(before1.RankPoints, winStreakBefore1)
                : 0;
            var winStreakEndedBounty1 = winnerIndex == 1
                ? CalculateWinStreakEndedBounty(before0.RankPoints, winStreakBefore0)
                : 0;
            var calculation0 = CalculateRankPoints(before0, before1, score0 > 0.5, resultStreak0, winStreakEndedBounty0);
            var calculation1 = CalculateRankPoints(before1, before0, score1 > 0.5, resultStreak1, winStreakEndedBounty1);
            var after0 = ApplyResult(before0, afterRating0, score0, calculation0);
            var after1 = ApplyResult(before1, afterRating1, score1, calculation1);

            Save(connection, transaction, after0);
            Save(connection, transaction, after1);
            InsertMatch(connection, transaction, matchId, season.Id, endedAtUtc, before0.AccountKey,
                before1.AccountKey, winnerIndex, after0.RankPoints - before0.RankPoints,
                after1.RankPoints - before1.RankPoints);
            InsertEvent(connection, transaction, matchId, season.Id, before0, after0, endedAtUtc);
            InsertEvent(connection, transaction, matchId, season.Id, before1, after1, endedAtUtc);
            var faction0 = ReadFaction(connection, transaction, after0.AccountKey);
            var faction1 = ReadFaction(connection, transaction, after1.AccountKey);
            var winStreak0 = winnerIndex == 0 ? winStreakBefore0 + 1 : 0;
            var winStreak1 = winnerIndex == 1 ? winStreakBefore1 + 1 : 0;
            transaction.Commit();
            return new RankedMatchSettlement(
                matchId,
                ToSettlement(player0Account, before0, after0, calculation0, faction0, FactionRank(connection, season, after0, faction0), winStreakBefore0, winStreak0, winStreakBefore1),
                ToSettlement(player1Account, before1, after1, calculation1, faction1, FactionRank(connection, season, after1, faction1), winStreakBefore1, winStreak1, winStreakBefore0));
        }
    }

    private static int CalculateWinStreakEndedBounty(int defeatedRankPoints, int endedWinStreak)
    {
        if (endedWinStreak < GlobalAnnouncementPolicy.RankedWinStreakAnnouncementThreshold) return 0;
        if (defeatedRankPoints < NewWorldRankPoints) return 0;

        return SettlementRulesFor(defeatedRankPoints).WinStreakCap;
    }

    private static SettlementRules SettlementRulesFor(int rankPoints)
        => rankPoints switch
        {
            >= TenBillionBountyRankPoints => new SettlementRules(250, 125, 63, 63),
            >= SixHundredMillionBountyRankPoints => new SettlementRules(150, 75, 38, 38),
            >= ThreeHundredMillionBountyRankPoints => new SettlementRules(80, 40, 20, 20),
            >= NewWorldRankPoints => new SettlementRules(40, 20, 10, 10),
            _ => new SettlementRules(20, 10, 5, 5),
        };

    private static RankPointCalculation CalculateRankPoints(
        Profile self,
        Profile opponent,
        bool won,
        int resultStreak,
        int winStreakEndedBounty)
    {
        if (self.PlacementGames < PlacementRequired)
            return new RankPointCalculation(0, 0, 0, self.RankPoints - opponent.RankPoints, 0, 0, resultStreak, won, false);

        var settlementRules = SettlementRulesFor(self.RankPoints);
        var baseDelta = won ? settlementRules.BaseDelta : -settlementRules.BaseDelta;
        var streakAdjustment = Math.Clamp(resultStreak - 1, 0,
            won ? settlementRules.WinStreakCap : settlementRules.LossStreakCap);
        // 未完成定级的对手没有可比较的可见 RP，不参与分差修正。
        var rankDifference = opponent.PlacementGames >= PlacementRequired
            ? self.RankPoints - opponent.RankPoints
            : 0;
        var rankDifferenceAdjustmentCap = settlementRules.RankDifferenceCap;
        var rankDifferenceAdjustment = rankDifference switch
        {
            < 0 => Math.Clamp((-rankDifference) / 100, 0, rankDifferenceAdjustmentCap),
            // 分差只用于奖励低悬赏玩家以下克上、减轻失败损失。
            // 高悬赏玩家维持本档位的基础胜负变化，不再因匹配到低悬赏对手而少赢或多扣。
            > 0 => 0,
            _ => 0,
        };
        return new RankPointCalculation(baseDelta, streakAdjustment, winStreakEndedBounty, rankDifference,
            rankDifferenceAdjustment, baseDelta + streakAdjustment + winStreakEndedBounty + rankDifferenceAdjustment,
            resultStreak, won, true);
    }

    private static Profile ApplyResult(Profile before, RatingUpdate afterRating, double score, RankPointCalculation calculation)
    {
        var placementGames = Math.Min(PlacementRequired, before.PlacementGames + 1);
        var games = before.Games + 1;
        var wins = before.Wins + (score > 0.5 ? 1 : 0);
        var losses = before.Losses + (score < 0.5 ? 1 : 0);
        var rankPoints = before.RankPoints;

        if (placementGames == PlacementRequired && before.PlacementGames < PlacementRequired)
        {
            // 定级最高黄金 I；隐藏分继续保留真实水平并用于后续追赶。
            rankPoints = Math.Clamp((int)Math.Round((afterRating.Rating - 1200) * 2), 0, 899);
        }
        else if (before.PlacementGames >= PlacementRequired)
        {
            // 可见 RP 按赛前悬赏档位取基础分，再叠加连续胜负和赛前可见 RP 分差修正。
            // Glicko 隐藏分仍独立更新且只用于匹配。
            var delta = calculation.IntendedDelta;
            if (score < 0.5 && before.RankPoints < 300) delta = 0; // 青铜不扣可见分
            rankPoints = Math.Max(0, before.RankPoints + delta);
            rankPoints = Math.Max(RankProtectionFloor(before.HighestRankPoints), rankPoints);
        }

        return before with
        {
            Rating = afterRating.Rating,
            RatingDeviation = afterRating.Deviation,
            Volatility = afterRating.Volatility,
            RankPoints = rankPoints,
            HighestRankPoints = Math.Max(before.HighestRankPoints, rankPoints),
            PlacementGames = placementGames,
            Games = games,
            Wins = wins,
            Losses = losses,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    private static int RankProtectionFloor(int highestRankPoints)
    {
        // 每个基础结算分变化的悬赏档位都是永久地板；达到后不再掉回上一档。
        foreach (var floor in PermanentBountyProtectionFloors)
            if (highestRankPoints >= floor) return floor;

        // 保留原有白银、黄金大段保护；白金至新世界前仍按既有规则完整升降。
        if (highestRankPoints >= 600 && highestRankPoints < 900) return 600;
        if (highestRankPoints >= 300 && highestRankPoints < 600) return 300;
        return 0;
    }

    private static Profile ResetRankProgress(Profile profile, DateTime resetAtUtc)
        => profile with
        {
            Rating = InitialRating,
            RatingDeviation = InitialDeviation,
            Volatility = InitialVolatility,
            RankPoints = 0,
            HighestRankPoints = 0,
            PlacementGames = 0,
            Games = 0,
            Wins = 0,
            Losses = 0,
            UpdatedAtUtc = resetAtUtc.ToUniversalTime(),
        };

    private static RatingUpdate UpdateRating(Profile self, Profile opponent, double score)
    {
        var mu = (self.Rating - 1500) / GlickoScale;
        var phi = self.RatingDeviation / GlickoScale;
        var muJ = (opponent.Rating - 1500) / GlickoScale;
        var phiJ = opponent.RatingDeviation / GlickoScale;
        var g = 1 / Math.Sqrt(1 + 3 * phiJ * phiJ / (Math.PI * Math.PI));
        var e = 1 / (1 + Math.Exp(-g * (mu - muJ)));
        var v = 1 / (g * g * e * (1 - e));
        var delta = v * g * (score - e);
        var sigmaPrime = SolveVolatility(phi, self.Volatility, delta, v);
        var phiStar = Math.Sqrt(phi * phi + sigmaPrime * sigmaPrime);
        var phiPrime = 1 / Math.Sqrt(1 / (phiStar * phiStar) + 1 / v);
        var muPrime = mu + phiPrime * phiPrime * g * (score - e);
        return new RatingUpdate(
            1500 + GlickoScale * muPrime,
            Math.Clamp(GlickoScale * phiPrime, 30, 350),
            sigmaPrime);
    }

    private static double SolveVolatility(double phi, double sigma, double delta, double v)
    {
        const double epsilon = 0.000001;
        var a = Math.Log(sigma * sigma);
        double F(double x)
        {
            var ex = Math.Exp(x);
            var top = ex * (delta * delta - phi * phi - v - ex);
            var bottom = 2 * Math.Pow(phi * phi + v + ex, 2);
            return top / bottom - (x - a) / (Tau * Tau);
        }

        var A = a;
        double B;
        if (delta * delta > phi * phi + v)
            B = Math.Log(delta * delta - phi * phi - v);
        else
        {
            var k = 1;
            while (F(a - k * Tau) < 0) k++;
            B = a - k * Tau;
        }

        var fA = F(A);
        var fB = F(B);
        while (Math.Abs(B - A) > epsilon)
        {
            var C = A + (A - B) * fA / (fB - fA);
            var fC = F(C);
            if (fC * fB <= 0) { A = B; fA = fB; }
            else fA /= 2;
            B = C;
            fB = fC;
        }
        return Math.Exp(A / 2);
    }

    private static RankPlayerSettlement ToSettlement(
        string account,
        Profile before,
        Profile after,
        RankPointCalculation calculation,
        string? faction,
        int? factionRank,
        int winStreakBefore,
        int winStreak,
        int opponentWinStreakBefore)
    {
        var (tier, division) = RankLabel(after.RankPoints, faction, factionRank);
        var actualDelta = after.RankPoints - before.RankPoints;
        var rankProtectionAdjustment = calculation.FormulaApplied
            ? actualDelta - calculation.IntendedDelta
            : 0;
        return new RankPlayerSettlement(account, before.RankPoints, after.RankPoints,
            actualDelta, calculation.BaseDelta, calculation.StreakAdjustment, calculation.WinStreakEndedBounty,
            calculation.WinStreakEndedBounty > 0 ? opponentWinStreakBefore : 0, calculation.RankDifference,
            calculation.RankDifferenceAdjustment, rankProtectionAdjustment, calculation.ResultStreak,
            calculation.Won, calculation.FormulaApplied, faction ?? string.Empty, tier, division, after.PlacementGames,
            PlacementRequired, before.PlacementGames < PlacementRequired && after.PlacementGames == PlacementRequired,
            winStreakBefore, winStreak);
    }

    private RankProfileSnapshot ToSnapshot(Profile profile, Season season, string? faction, int? factionRank,
        string account, DateTime? nowUtc)
    {
        var (tier, division) = RankLabel(profile.RankPoints, faction, factionRank);
        IReadOnlyList<string> championLeaderNumbers;
        try
        {
            championLeaderNumbers = _championStore.GetChampionLeaderNumbers(account, nowUtc);
        }
        catch
        {
            // 称号数据源暂不可用时只隐藏称号，不影响排位资料加载。
            championLeaderNumbers = Array.Empty<string>();
        }
        return new RankProfileSnapshot(season.Id, season.StartsAtUtc, season.EndsAtUtc,
            profile.PlacementGames, PlacementRequired, profile.RankPoints, faction, tier, division,
            profile.Games, profile.Wins, profile.Losses, profile.HighestRankPoints, championLeaderNumbers);
    }

    public static (string Tier, int? Division) RankLabel(int rankPoints, string? faction = null, int? factionRank = null)
    {
        if (rankPoints >= NewWorldRankPoints) return (NewWorldTitle(faction, factionRank), null);
        var tiers = faction switch
        {
            PirateFaction => new[] { "见习海贼", "海贼战斗员", "海贼干部", "副船长", "船长" },
            MarineFaction => new[] { "海军三等兵", "海军少尉", "海军少校", "海军少将", "海军中将" },
            GovernmentFaction => new[] { "政府线人", "初级特工", "CP9 特工", "CP0 特工", "浅海契约" },
            _ => new[] { "未选择阵营", "未选择阵营", "未选择阵营", "未选择阵营", "未选择阵营" },
        };
        var tierIndex = Math.Clamp(rankPoints / 300, 0, tiers.Length - 1);
        var within = Math.Clamp(rankPoints - tierIndex * 300, 0, 299);
        return (tiers[tierIndex], 3 - within / 100);
    }

    private static string NewWorldTitle(string? faction, int? factionRank) => (faction, factionRank) switch
    {
        (PirateFaction, 1) => "海贼王",
        (PirateFaction, >= 2 and <= 5) => "四皇",
        (MarineFaction, 1) => "海军元帅",
        (MarineFaction, >= 2 and <= 4) => "海军大将",
        (GovernmentFaction, 1) => "世界之王",
        (GovernmentFaction, >= 2 and <= 6) => "五老星",
        (PirateFaction, _) => "超新星",
        (MarineFaction, _) => "大将候补",
        (GovernmentFaction, _) => "神之骑士团",
        _ => "新世界",
    };

    private static string? NormalizeFaction(string? faction) => faction?.Trim().ToLowerInvariant() switch
    {
        PirateFaction => PirateFaction,
        MarineFaction => MarineFaction,
        GovernmentFaction => GovernmentFaction,
        _ => null,
    };

    private static Season SeasonAt(DateTime utc)
    {
        utc = utc.ToUniversalTime();
        var index = Math.Max(1, (int)Math.Floor((utc - SeasonAnchorUtc).TotalDays / SeasonLength.TotalDays) + 1);
        var start = SeasonAnchorUtc.AddTicks(SeasonLength.Ticks * (index - 1L));
        return new Season($"S{index}", start, start.Add(SeasonLength));
    }

    public bool TryRefreshLeaderboardSnapshot(DateTime? nowUtc = null)
    {
        Initialize();
        if (!_leaderboardRefreshGate.Wait(0)) return false;

        try
        {
            Volatile.Write(ref _lastLeaderboardRefreshError, null);
            var observedAtUtc = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
            BeforeLeaderboardSnapshotBuildForTesting?.Invoke();
            var candidate = BuildPublicLeaderboardSnapshot(observedAtUtc);
            ValidatePublicLeaderboardSnapshot(candidate);

            // 真实时钟跨越赛季边界时丢弃旧赛季候选，不让它成为新的可见版本。
            if (nowUtc is null && !string.Equals(
                    candidate.SeasonId,
                    SeasonAt(DateTime.UtcNow).Id,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("赛季已切换，丢弃旧赛季排位榜候选快照");

            var version = ReserveNextSnapshotVersion();
            var published = new PublicLeaderboardSnapshot(
                candidate.SeasonId,
                version,
                candidate.GeneratedAtUtc,
                Array.AsReadOnly(candidate.Items.ToArray()),
                Array.AsReadOnly(candidate.FactionStandings.ToArray()));
            Volatile.Write(ref _publicLeaderboardSnapshot, published);
            Volatile.Write(ref _lastLeaderboardRefreshError, null);
            return true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastLeaderboardRefreshError, ex.Message);
            return false;
        }
        finally
        {
            _leaderboardRefreshGate.Release();
        }
    }

    public async Task RunLeaderboardRefreshLoopAsync(
        TimeSpan interval,
        TimeSpan initialDelay,
        CancellationToken cancellationToken,
        Action<string>? logError = null)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay));

        try
        {
            if (initialDelay > TimeSpan.Zero) await Task.Delay(initialDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryRefreshLeaderboardSnapshot() && LastLeaderboardRefreshError is { Length: > 0 } error)
                    logError?.Invoke(error);
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常关服：取消后不再启动新一轮快照。
        }
    }

    private PublicLeaderboardSnapshot GetCurrentPublicSnapshot(Season season, DateTime observedAtUtc)
    {
        var current = Volatile.Read(ref _publicLeaderboardSnapshot);
        if (current is not null && string.Equals(current.SeasonId, season.Id, StringComparison.Ordinal))
            return current;

        // 启动预热失败或赛季刚切换时，首个请求允许单次同步补热。
        TryRefreshLeaderboardSnapshot(observedAtUtc);
        current = Volatile.Read(ref _publicLeaderboardSnapshot);
        if (current is not null && string.Equals(current.SeasonId, season.Id, StringComparison.Ordinal))
            return current;

        throw new RankLeaderboardUnavailableException(
            LastLeaderboardRefreshError is { Length: > 0 } error
                ? $"排位榜快照暂时不可用：{error}"
                : "排位榜正在生成，请稍后重试");
    }

    private RankSnapshot CombineSnapshot(
        RealtimePlayer player,
        PublicLeaderboardSnapshot publicSnapshot,
        string account,
        DateTime? nowUtc)
    {
        var leaderboard = publicSnapshot.Items
            .Where(entry => entry.Item.Rank <= 100
                || entry.Item.FactionRank <= 100
                || string.Equals(entry.AccountKey, player.Profile.AccountKey, StringComparison.Ordinal))
            .Select(entry => entry.Item with
            {
                IsCurrentPlayer = string.Equals(entry.AccountKey, player.Profile.AccountKey, StringComparison.Ordinal),
            })
            .ToArray();
        return new RankSnapshot(
            ToSnapshot(player.Profile, player.Season, player.Faction, player.FactionRank, account, nowUtc),
            leaderboard,
            publicSnapshot.FactionStandings,
            publicSnapshot.Version,
            publicSnapshot.GeneratedAtUtc);
    }

    private PublicLeaderboardCandidate BuildPublicLeaderboardSnapshot(DateTime observedAtUtc)
    {
        var season = SeasonAt(observedAtUtc);
        List<RankedLeaderboardEntry> entries;
        IReadOnlyList<FactionStanding> factionStandings;
        using (var connection = Open())
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            entries = ReadRankedLeaderboardEntries(connection, transaction, season);
            factionStandings = ReadFactionStandings(connection, transaction, season);
            transaction.Commit();
        }

        // 外部统计是快照的必要组成部分；任一数据源失败都放弃候选，继续服务上一版。
        var accountKeys = entries.Select(entry => entry.AccountKey).ToArray();
        var favoriteLeaders = _leaderStatsStore.GetFavoriteLeaders(accountKeys);
        var championLeaderNumbersByPlayer = _championStore.GetChampionLeaderNumbersByPlayerKeys(
            accountKeys,
            observedAtUtc);

        var items = entries.Select(entry =>
        {
            var label = RankLabel(entry.RankPoints, entry.Faction, entry.FactionRank);
            favoriteLeaders.TryGetValue(entry.AccountKey, out var favoriteLeader);
            championLeaderNumbersByPlayer.TryGetValue(entry.AccountKey, out var championLeaderNumbers);
            return new CachedRankLeaderboardItem(
                entry.AccountKey,
                new RankLeaderboardItem(
                    entry.GlobalRank,
                    entry.FactionRank,
                    entry.DisplayName,
                    entry.RankPoints,
                    entry.Faction,
                    label.Tier,
                    label.Division,
                    entry.Games,
                    entry.Wins,
                    entry.Games == 0 ? 0 : Math.Round(entry.Wins * 100d / entry.Games, 1),
                    favoriteLeader?.LeaderNumber,
                    championLeaderNumbers ?? Array.Empty<string>(),
                    false));
        }).ToArray();

        return new PublicLeaderboardCandidate(
            season.Id,
            observedAtUtc,
            items,
            factionStandings);
    }

    private static List<RankedLeaderboardEntry> ReadRankedLeaderboardEntries(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Season season)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH eligible AS (
                SELECT p.account_key, p.display_name, p.rank_points, p.games, p.wins, f.faction,
                       p.rating, p.rating_deviation, p.updated_at_utc
                FROM rank_profiles p
                JOIN rank_factions f ON f.account_key=p.account_key
                WHERE p.season_id=$season AND p.placement_games >= $placements
            ), ranked AS (
                SELECT *,
                       ROW_NUMBER() OVER (ORDER BY rank_points DESC, (rating - 2 * rating_deviation) DESC, updated_at_utc ASC, account_key ASC) AS global_rank,
                       ROW_NUMBER() OVER (PARTITION BY faction ORDER BY rank_points DESC, (rating - 2 * rating_deviation) DESC, updated_at_utc ASC, account_key ASC) AS faction_rank
                FROM eligible
            )
            SELECT account_key, display_name, rank_points, games, wins, faction, global_rank, faction_rank
            FROM ranked
            ORDER BY global_rank ASC;
            """;
        command.Parameters.AddWithValue("$season", season.Id);
        command.Parameters.AddWithValue("$placements", PlacementRequired);
        using var reader = command.ExecuteReader();
        var result = new List<RankedLeaderboardEntry>();
        while (reader.Read())
        {
            result.Add(new RankedLeaderboardEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }
        return result;
    }

    private static IReadOnlyList<FactionStanding> ReadFactionStandings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Season season)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH totals AS (
                SELECT f.faction,
                       SUM(p.rank_points) AS total_points,
                       COUNT(*) AS player_count,
                       SUM(p.games) AS games,
                       SUM(p.wins) AS wins
                FROM rank_profiles p
                JOIN rank_factions f ON f.account_key=p.account_key
                WHERE p.season_id=$season AND p.placement_games >= $placements
                GROUP BY f.faction
            )
            SELECT faction, total_points, player_count, games, wins,
                   ROW_NUMBER() OVER (ORDER BY total_points DESC, wins DESC, faction ASC) AS faction_rank
            FROM totals
            ORDER BY faction_rank;
            """;
        command.Parameters.AddWithValue("$season", season.Id);
        command.Parameters.AddWithValue("$placements", PlacementRequired);
        using var reader = command.ExecuteReader();
        var result = new List<FactionStanding>();
        while (reader.Read())
        {
            result.Add(new FactionStanding(
                reader.GetInt32(5), reader.GetString(0), reader.GetInt64(1),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return result;
    }

    private long ReserveNextSnapshotVersion()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rank_public_snapshot_state(id,last_version) VALUES(1,1)
            ON CONFLICT(id) DO UPDATE SET last_version=last_version+1
            RETURNING last_version;
            """;
        var version = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        transaction.Commit();
        return version;
    }

    private static void ValidatePublicLeaderboardSnapshot(PublicLeaderboardCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.SeasonId))
            throw new InvalidOperationException("排位榜候选快照缺少赛季");
        if (candidate.GeneratedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("排位榜候选快照时间必须为 UTC");

        var accountKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < candidate.Items.Count; index++)
        {
            var entry = candidate.Items[index];
            if (!accountKeys.Add(entry.AccountKey))
                throw new InvalidOperationException("排位榜候选快照包含重复玩家");
            if (entry.Item.Rank != index + 1 || entry.Item.FactionRank <= 0 || entry.Item.IsCurrentPlayer)
                throw new InvalidOperationException("排位榜候选快照名次不完整");
        }

        var factions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var standing in candidate.FactionStandings)
        {
            if (!factions.Add(standing.Faction) || standing.Rank <= 0)
                throw new InvalidOperationException("排位榜候选快照阵营汇总不完整");
        }
    }

    private static int? FactionRank(
        SqliteConnection connection,
        Season season,
        Profile profile,
        string? faction,
        SqliteTransaction? transaction = null)
    {
        if (faction is null || profile.PlacementGames < PlacementRequired) return null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) + 1
            FROM rank_profiles p
            JOIN rank_factions f ON f.account_key=p.account_key
            WHERE p.season_id=$season AND f.faction=$faction AND p.placement_games >= $placements
              AND (
                p.rank_points > $points
                OR (p.rank_points = $points AND (p.rating - 2 * p.rating_deviation) > $conservativeRating)
                OR (p.rank_points = $points AND (p.rating - 2 * p.rating_deviation) = $conservativeRating AND p.updated_at_utc < $updated)
                OR (p.rank_points = $points AND (p.rating - 2 * p.rating_deviation) = $conservativeRating AND p.updated_at_utc = $updated AND p.account_key < $key)
              );
            """;
        command.Parameters.AddWithValue("$season", season.Id);
        command.Parameters.AddWithValue("$faction", faction);
        command.Parameters.AddWithValue("$placements", PlacementRequired);
        command.Parameters.AddWithValue("$points", profile.RankPoints);
        command.Parameters.AddWithValue("$conservativeRating", profile.Rating - 2 * profile.RatingDeviation);
        command.Parameters.AddWithValue("$updated", profile.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$key", profile.AccountKey);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string? ReadFaction(SqliteConnection connection, SqliteTransaction transaction, string accountKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT faction FROM rank_factions WHERE account_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", accountKey);
        return command.ExecuteScalar() as string;
    }

    private Profile LoadOrCreate(SqliteConnection connection, SqliteTransaction transaction, Season season,
        string account, string displayName)
    {
        var key = HashAccount(account);
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT display_name, rating, rating_deviation, volatility, rank_points,
                       highest_rank_points, placement_games, games, wins, losses, updated_at_utc
                FROM rank_profiles WHERE season_id=$season AND account_key=$key;
                """;
            read.Parameters.AddWithValue("$season", season.Id);
            read.Parameters.AddWithValue("$key", key);
            using var reader = read.ExecuteReader();
            if (reader.Read())
                return new Profile(season.Id, key, reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2),
                    reader.GetDouble(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                    reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
                    DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        var created = new Profile(season.Id, key, displayName, InitialRating, InitialDeviation,
            InitialVolatility, 0, 0, 0, 0, 0, 0, DateTime.UtcNow);
        Save(connection, transaction, created);
        return created;
    }

    private static void Save(SqliteConnection connection, SqliteTransaction transaction, Profile profile)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rank_profiles (
                season_id, account_key, display_name, rating, rating_deviation, volatility,
                rank_points, highest_rank_points, placement_games, games, wins, losses, updated_at_utc)
            VALUES ($season,$key,$name,$rating,$rd,$volatility,$rp,$highest,$placements,$games,$wins,$losses,$updated)
            ON CONFLICT(season_id, account_key) DO UPDATE SET
                display_name=excluded.display_name, rating=excluded.rating,
                rating_deviation=excluded.rating_deviation, volatility=excluded.volatility,
                rank_points=excluded.rank_points, highest_rank_points=excluded.highest_rank_points,
                placement_games=excluded.placement_games, games=excluded.games,
                wins=excluded.wins, losses=excluded.losses, updated_at_utc=excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$season", profile.SeasonId);
        command.Parameters.AddWithValue("$key", profile.AccountKey);
        command.Parameters.AddWithValue("$name", profile.DisplayName);
        command.Parameters.AddWithValue("$rating", profile.Rating);
        command.Parameters.AddWithValue("$rd", profile.RatingDeviation);
        command.Parameters.AddWithValue("$volatility", profile.Volatility);
        command.Parameters.AddWithValue("$rp", profile.RankPoints);
        command.Parameters.AddWithValue("$highest", profile.HighestRankPoints);
        command.Parameters.AddWithValue("$placements", profile.PlacementGames);
        command.Parameters.AddWithValue("$games", profile.Games);
        command.Parameters.AddWithValue("$wins", profile.Wins);
        command.Parameters.AddWithValue("$losses", profile.Losses);
        command.Parameters.AddWithValue("$updated", profile.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static bool MatchExists(SqliteConnection connection, SqliteTransaction transaction, string matchId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM ranked_matches WHERE match_id=$match LIMIT 1;";
        command.Parameters.AddWithValue("$match", matchId);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>读取本赛季从最新一局向前连续获胜的场数；一场失利即中断。</summary>
    private static int CurrentWinStreak(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonId,
        string accountKey)
        => CurrentResultStreak(connection, transaction, seasonId, accountKey, countWins: true);

    private static int CurrentResultStreak(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonId,
        string accountKey,
        bool countWins)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT winner_index, player0_key, player1_key
            FROM ranked_matches
            WHERE season_id=$season AND (player0_key=$key OR player1_key=$key)
            ORDER BY ended_at_utc DESC, rowid DESC;
            """;
        command.Parameters.AddWithValue("$season", seasonId);
        command.Parameters.AddWithValue("$key", accountKey);

        using var reader = command.ExecuteReader();
        var streak = 0;
        while (reader.Read())
        {
            var winnerIndex = reader.GetInt32(0);
            var won = (winnerIndex == 0 && string.Equals(reader.GetString(1), accountKey, StringComparison.Ordinal))
                || (winnerIndex == 1 && string.Equals(reader.GetString(2), accountKey, StringComparison.Ordinal));
            if (won != countWins) break;
            streak++;
        }
        return streak;
    }

    /// <summary>读取本赛季从最新一局向前连续失败的场数；一场胜利即中断。</summary>
    private static int CurrentLossStreak(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonId,
        string accountKey)
        => CurrentResultStreak(connection, transaction, seasonId, accountKey, countWins: false);

    private static void InsertMatch(SqliteConnection connection, SqliteTransaction transaction, string matchId,
        string seasonId, DateTime endedAtUtc, string p0, string p1, int winner, int delta0, int delta1)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ranked_matches(match_id,season_id,ended_at_utc,player0_key,player1_key,winner_index,player0_rp_delta,player1_rp_delta)
            VALUES($match,$season,$ended,$p0,$p1,$winner,$d0,$d1);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$season", seasonId);
        command.Parameters.AddWithValue("$ended", endedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$p0", p0);
        command.Parameters.AddWithValue("$p1", p1);
        command.Parameters.AddWithValue("$winner", winner);
        command.Parameters.AddWithValue("$d0", delta0);
        command.Parameters.AddWithValue("$d1", delta1);
        command.ExecuteNonQuery();
    }

    private static void InsertEvent(SqliteConnection connection, SqliteTransaction transaction, string matchId,
        string seasonId, Profile before, Profile after, DateTime endedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rank_rating_events(match_id,season_id,account_key,rating_before,rating_after,rp_before,rp_after,created_at_utc)
            VALUES($match,$season,$key,$rb,$ra,$pb,$pa,$created);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$season", seasonId);
        command.Parameters.AddWithValue("$key", before.AccountKey);
        command.Parameters.AddWithValue("$rb", before.Rating);
        command.Parameters.AddWithValue("$ra", after.Rating);
        command.Parameters.AddWithValue("$pb", before.RankPoints);
        command.Parameters.AddWithValue("$pa", after.RankPoints);
        command.Parameters.AddWithValue("$created", endedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string HashAccount(string account)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant()))).ToLowerInvariant();

    private sealed record Season(string Id, DateTime StartsAtUtc, DateTime EndsAtUtc);
    private sealed record RealtimePlayer(Profile Profile, Season Season, string? Faction, int? FactionRank);
    private sealed record RankedLeaderboardEntry(
        string AccountKey,
        string DisplayName,
        int RankPoints,
        int Games,
        int Wins,
        string Faction,
        int GlobalRank,
        int FactionRank);
    private sealed record CachedRankLeaderboardItem(string AccountKey, RankLeaderboardItem Item);
    private sealed record PublicLeaderboardCandidate(
        string SeasonId,
        DateTime GeneratedAtUtc,
        IReadOnlyList<CachedRankLeaderboardItem> Items,
        IReadOnlyList<FactionStanding> FactionStandings);
    private sealed record PublicLeaderboardSnapshot(
        string SeasonId,
        long Version,
        DateTime GeneratedAtUtc,
        IReadOnlyList<CachedRankLeaderboardItem> Items,
        IReadOnlyList<FactionStanding> FactionStandings);
    private sealed record RatingUpdate(double Rating, double Deviation, double Volatility);
    private sealed record SettlementRules(
        int BaseDelta,
        int WinStreakCap,
        int LossStreakCap,
        int RankDifferenceCap);
    private sealed record RankPointCalculation(
        int BaseDelta,
        int StreakAdjustment,
        int WinStreakEndedBounty,
        int RankDifference,
        int RankDifferenceAdjustment,
        int IntendedDelta,
        int ResultStreak,
        bool Won,
        bool FormulaApplied);
    private sealed record Profile(
        string SeasonId, string AccountKey, string DisplayName,
        double Rating, double RatingDeviation, double Volatility,
        int RankPoints, int HighestRankPoints, int PlacementGames,
        int Games, int Wins, int Losses, DateTime UpdatedAtUtc);
}
