using Microsoft.Data.Sqlite;

namespace GrandUMI.Game.Ranked;

public sealed partial class RankedStore
{
    // 北京时间 12 月 1 日零点截止；届时冻结 S2，下一季必须另行显式发布。
    private static readonly Season HunterSeason = new("S2",
        new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 30, 16, 0, 0, DateTimeKind.Utc));
    internal bool IsHunterSeason => _bountySettlementMode == RankedBountySettlementMode.HuntersSeasonTwo;
    public static bool IsSea(string? value) => value is "east" or "west" or "south" or "north";
    public static string HunterTier(int heads) => heads switch
    {
        >= 10_000 => "万人斩", >= 1_000 => "千人斩", >= 100 => "百人斩", >= 10 => "十人斩", _ => "见习猎人",
    };
    private Season WalletSeasonAt(DateTime utc) => IsHunterSeason ? NaturalSeasonAt(FrozenBountySeasonReferenceUtc) : SeasonAt(utc);
    private string AffiliationTable(Season season) => IsHunterSeason && season.Id == "S2"
        ? "(SELECT account_key,faction FROM hunter_regions WHERE season_id=$season)" : "rank_factions";

    private void InitializeHunterSeason(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        using var schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS hunter_regions (
                season_id TEXT NOT NULL, account_key TEXT NOT NULL, faction TEXT NOT NULL
                CHECK(faction IN ('east','west','south','north')), PRIMARY KEY(season_id,account_key));
            CREATE TABLE IF NOT EXISTS rank_season_honors (
                season_id TEXT NOT NULL, account_key TEXT NOT NULL, title TEXT NOT NULL,
                faction_rank INTEGER NOT NULL, awarded_at_utc TEXT NOT NULL,
                PRIMARY KEY(season_id,account_key));
            CREATE TABLE IF NOT EXISTS rank_season_settlements (
                season_id TEXT PRIMARY KEY, settled_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS rank_admin_test_honors (
                season_id TEXT NOT NULL CHECK(season_id='S1'),
                account_key TEXT NOT NULL, title TEXT NOT NULL CHECK(title IN (
                    'S1 海贼王','S1 四皇','S1 海军元帅','S1 海军大将','S1 世界之王','S1 五老星')),
                granted_at_utc TEXT NOT NULL,
                PRIMARY KEY(season_id,account_key,title));
            """;
        schema.ExecuteNonQuery();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM rank_season_settlements WHERE season_id='S1';";
        if (check.ExecuteScalar() is null)
        {
            // 与旧榜单使用完全相同的排序和定级资格；一次性固化，不受后续改名或赛季影响。
            using var award = connection.CreateCommand();
            award.Transaction = transaction;
            award.CommandText = """
                WITH ranked AS (
                    SELECT p.account_key,f.faction,
                        ROW_NUMBER() OVER(PARTITION BY f.faction ORDER BY p.rank_points DESC,
                            (p.rating-2*p.rating_deviation) DESC,p.updated_at_utc ASC,p.account_key ASC) AS place
                    FROM rank_profiles p JOIN rank_factions f ON f.account_key=p.account_key
                    WHERE p.season_id='S1' AND p.placement_games>=5
                )
                INSERT INTO rank_season_honors(season_id,account_key,title,faction_rank,awarded_at_utc)
                SELECT 'S1',account_key,'S1 ' || CASE
                    WHEN faction='pirate' AND place=1 THEN '海贼王'
                    WHEN faction='pirate' THEN '四皇'
                    WHEN faction='marine' AND place=1 THEN '海军元帅'
                    WHEN faction='marine' THEN '海军大将'
                    WHEN faction='government' AND place=1 THEN '世界之王'
                    ELSE '五老星' END,place,$now
                FROM ranked WHERE (faction='pirate' AND place<=5)
                    OR (faction='marine' AND place<=4) OR (faction='government' AND place<=6);
                INSERT INTO rank_season_settlements VALUES('S1',$now);
                """;
            award.Parameters.AddWithValue("$now", ChatDecorationUtcText(DateTime.UtcNow));
            award.ExecuteNonQuery();
            if (_chatDecorationExchangeEnabled)
            {
                // 补齐未打开过背包的玩家；已消费的余额只补未入账的峰值差额，绝不重发或清空。
                var profiles = new List<(string Key, string Name)>();
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT account_key,display_name FROM rank_profiles WHERE season_id='S1';";
                    using var reader = read.ExecuteReader();
                    while (reader.Read()) profiles.Add((reader.GetString(0), reader.GetString(1)));
                }
                foreach (var (key, name) in profiles)
                {
                    // LoadOrCreate 接受明文账号，迁移必须按已有匿名键读取，不能重复散列。
                    using var read = connection.CreateCommand();
                    read.Transaction = transaction;
                    read.CommandText = "SELECT rank_points,highest_rank_points FROM rank_profiles WHERE season_id='S1' AND account_key=$key;";
                    read.Parameters.AddWithValue("$key", key);
                    using var reader = read.ExecuteReader();
                    reader.Read();
                    var current = reader.GetInt32(0); var peak = reader.GetInt32(1);
                    reader.Close();
                    var profile = new Profile("S1", key, name, InitialRating, InitialDeviation,
                        InitialVolatility, current, peak, 5, 0, 0, 0, DateTime.UtcNow);
                    ReadOrCreateChatDecorationWallet(connection, transaction, profile, DateTime.UtcNow);
                }
            }
        }
        transaction.Commit();
    }

    private string? ReadAffiliation(SqliteConnection connection, SqliteTransaction transaction, string seasonId, string key)
    {
        if (!IsHunterSeason || seasonId != "S2") return ReadFaction(connection, transaction, key);
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT faction FROM hunter_regions WHERE season_id='S2' AND account_key=$key;";
        read.Parameters.AddWithValue("$key", key);
        return read.ExecuteScalar() as string;
    }

    private IReadOnlyList<string> ReadSeasonTitles(string key)
    {
        if (!IsHunterSeason) return Array.Empty<string>();
        using var connection = Open();
        using var read = CreateSeasonTitlesCommand(connection, null, key);
        var titles = new List<string>();
        using var reader = read.ExecuteReader();
        while (reader.Read()) titles.Add(reader.GetString(1));
        return titles;
    }

    private SqliteCommand CreateSeasonTitlesCommand(
        SqliteConnection connection, SqliteTransaction? transaction, string? key = null)
    {
        var read = connection.CreateCommand();
        read.Transaction = transaction;
        var sources = "SELECT season_id,account_key,title FROM rank_season_honors";
        if (_testSeasonHonorsEnabled)
        {
            // 测试授予独立存储；必须同时启用测试服开关且仍属于管理员白名单。
            // UNION 去重，管理员原本赢得的赛季荣誉也只显示一次。
            var accounts = AdministratorPolicy.GetAuthorizedAccounts();
            var parameters = accounts.Select((account, i) =>
            {
                var parameter = $"$admin{i}";
                read.Parameters.AddWithValue(parameter, HashAccount(account));
                return parameter;
            }).ToArray();
            sources += $" UNION SELECT season_id,account_key,title FROM rank_admin_test_honors WHERE account_key IN ({string.Join(',', parameters)})";
        }
        read.CommandText = $"SELECT account_key,title FROM ({sources})";
        if (key is not null)
        {
            read.CommandText += " WHERE account_key=$key";
            read.Parameters.AddWithValue("$key", key);
        }
        read.CommandText += " ORDER BY season_id,title;";
        return read;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> ReadAllSeasonTitles(
        SqliteConnection connection, SqliteTransaction transaction)
    {
        var titles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (IsHunterSeason)
        {
            using var read = CreateSeasonTitlesCommand(connection, transaction);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!titles.TryGetValue(key, out var list)) titles[key] = list = new List<string>();
                list.Add(reader.GetString(1));
            }
        }
        return titles.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    private RankSnapshot? SelectHunterSea(string account, string? name, string sea, DateTime? nowUtc)
    {
        sea = sea.Trim().ToLowerInvariant();
        if (!IsSea(sea)) return null;
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        lock (_gate)
        {
            Initialize();
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var profile = LoadOrCreate(connection, transaction, HunterSeason, account, name ?? account);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO hunter_regions VALUES('S2',$key,$sea);";
            insert.Parameters.AddWithValue("$key", profile.AccountKey);
            insert.Parameters.AddWithValue("$sea", sea);
            insert.ExecuteNonQuery();
            transaction.Commit();
        }
        // 海域本赛季锁定，避免随领先海域改变归属以利用加成。
        return GetSnapshot(account, name, now);
    }

    private Profile InheritHunterRating(SqliteConnection connection, SqliteTransaction transaction, Profile profile)
    {
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT rating,rating_deviation,volatility FROM rank_profiles WHERE season_id='S1' AND account_key=$key;";
        read.Parameters.AddWithValue("$key", profile.AccountKey);
        using var reader = read.ExecuteReader();
        return reader.Read() ? profile with { Rating = reader.GetDouble(0), RatingDeviation = reader.GetDouble(1),
            Volatility = reader.GetDouble(2), PlacementGames = PlacementRequired } : profile with { PlacementGames = PlacementRequired };
    }

    private RankedMatchSettlement RecordHunterMatch(SqliteConnection connection, SqliteTransaction transaction,
        Season season, string matchId, DateTime endedAt, Profile before0, Profile before1,
        string account0, string account1, int winner)
    {
        var sea0 = ReadAffiliation(connection, transaction, season.Id, before0.AccountKey);
        var sea1 = ReadAffiliation(connection, transaction, season.Id, before1.AccountKey);
        if (sea0 is null || sea1 is null) throw new InvalidOperationException("赏金猎人排位双方必须先选择出海海域。");
        var streak0 = CurrentWinStreak(connection, transaction, season.Id, before0.AccountKey);
        var streak1 = CurrentWinStreak(connection, transaction, season.Id, before1.AccountKey);
        var winningStreak = (winner == 0 ? streak0 : streak1) + 1;
        var endedStreak = winner == 0 ? streak1 : streak0;
        var standings = ReadFactionStandings(connection, transaction, season);
        var top = standings.Count == 0 ? 0 : standings.Max(s => s.TotalRankPoints);
        var opponentSea = winner == 0 ? sea1 : sea0;
        var seaBonus = top > 0 && standings.Any(s => s.Faction == opponentSea && s.TotalRankPoints == top) ? 1 : 0;
        var active = endedAt.ToUniversalTime() >= season.StartsAtUtc && endedAt.ToUniversalTime() < season.EndsAtUtc;
        var streakBonus = Math.Max(0, winningStreak - 2);
        var endBonus = endedStreak >= 3 ? 2 : 0;
        var delta = active ? checked(1 + streakBonus + endBonus + seaBonus) : 0;
        var after0 = ApplyHunterResult(before0, before1, winner == 0, winner == 0 ? delta : 0, endedAt);
        var after1 = ApplyHunterResult(before1, before0, winner == 1, winner == 1 ? delta : 0, endedAt);
        Save(connection, transaction, after0); Save(connection, transaction, after1);
        InsertMatch(connection, transaction, matchId, season.Id, endedAt, before0.AccountKey, before1.AccountKey,
            winner, after0.RankPoints-before0.RankPoints, after1.RankPoints-before1.RankPoints);
        InsertEvent(connection, transaction, matchId, season.Id, before0, after0, endedAt);
        InsertEvent(connection, transaction, matchId, season.Id, before1, after1, endedAt);
        var rank0 = FactionRank(connection, season, after0, sea0, transaction);
        var rank1 = FactionRank(connection, season, after1, sea1, transaction);
        transaction.Commit();
        RankPlayerSettlement Result(string account, Profile before, Profile after, string sea, int? rank, bool won, int streak)
        {
            var formula = new RankPointCalculation(won && active ? 1 : 0, won && active ? streakBonus : 0,
                won && active ? endBonus : 0, 0, won && active ? seaBonus : 0, won ? delta : 0,
                won ? winningStreak : 0, won, active);
            return ToSettlement(account, before, after, formula, sea, rank, streak, won ? winningStreak : 0,
                won ? endedStreak : 0) with { PlacementRequired = 0, PlacementCompleted = false, IsHunterSeason = true };
        }
        return new RankedMatchSettlement(matchId, Result(account0,before0,after0,sea0,rank0,winner==0,streak0),
            Result(account1,before1,after1,sea1,rank1,winner==1,streak1));
    }

    private static Profile ApplyHunterResult(Profile before, Profile opponent, bool won, int delta, DateTime now)
    {
        var rating = UpdateRating(before, opponent, won ? 1 : 0);
        var heads = checked(before.RankPoints + delta);
        return before with { Rating = rating.Rating, RatingDeviation = rating.Deviation, Volatility = rating.Volatility,
            RankPoints = heads, HighestRankPoints = Math.Max(before.HighestRankPoints, heads),
            Games = checked(before.Games+1), Wins = checked(before.Wins+(won?1:0)),
            Losses = checked(before.Losses+(won?0:1)), UpdatedAtUtc = now };
    }
}
