using Microsoft.Data.Sqlite;

namespace GrandUMI.Game.Ranked;

public sealed class SeasonTitleValidationException(string message) : Exception(message);

public sealed partial class RankedStore
{
    /// <summary>公开名字旁的称号只读取已有佩戴和所有权，不创建排位档案或钱包。</summary>
    public string? GetPublicEquippedSeasonTitle(string account)
        => GetPublicEquippedSeasonTitles(new[] { account }).GetValueOrDefault(account);

    public IReadOnlyDictionary<string, string?> GetPublicEquippedSeasonTitles(IReadOnlyList<string> accounts)
    {
        if (accounts.Count > 40) throw new SeasonTitleValidationException("每次最多查询 40 个玩家称号。");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!IsHunterSeason) return result;
        lock (_gate)
        {
            Initialize();
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            var owned = ReadAllSeasonTitles(connection, transaction);
            transaction.Commit();
            var equipment = ReadSeasonTitleEquipment();
            foreach (var account in accounts.Where(account => !string.IsNullOrWhiteSpace(account)).Distinct(StringComparer.Ordinal))
                result[account] = ValidEquippedTitle(HashAccount(account), owned, equipment.Titles);
        }
        return result;
    }

    private SqliteConnection OpenSeasonTitleDatabase(string path, bool writable = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = writable ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString());
        connection.Open();
        return connection;
    }

    private void InitializeSeasonTitleEquipment()
    {
        // 所有模式共用标准排位库的佩戴设置，拥有的称号从两种模式合并读取。
        Directory.CreateDirectory(Path.GetDirectoryName(_seasonTitleEquipmentDatabasePath)!);
        using var connection = OpenSeasonTitleDatabase(_seasonTitleEquipmentDatabasePath, writable: true);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS rank_season_title_equipment (
                account_key TEXT PRIMARY KEY, title TEXT NULL, updated_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS rank_season_title_equipment_state (
                id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL CHECK(revision>=0));
            INSERT OR IGNORE INTO rank_season_title_equipment_state VALUES(1,0);
            """;
        schema.ExecuteNonQuery();
    }

    private static bool HasSeasonTitleTable(SqliteConnection connection, SqliteTransaction? transaction, string name)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
        check.Parameters.AddWithValue("$name", name);
        return check.ExecuteScalar() is not null;
    }

    private IEnumerable<(string Key, string Title)> ReadOtherSeasonTitles(string? key = null)
    {
        if (!IsHunterSeason || _otherSeasonTitleDatabasePath is null
            || string.Equals(_otherSeasonTitleDatabasePath, _databasePath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(_otherSeasonTitleDatabasePath)) yield break;
        using var connection = OpenSeasonTitleDatabase(_otherSeasonTitleDatabasePath);
        if (!HasSeasonTitleTable(connection, null, "rank_season_honors")) yield break;
        using var command = CreateSeasonTitlesCommand(connection, null, key);
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return (reader.GetString(0), reader.GetString(1));
    }

    public void EquipSeasonTitle(string account, string? title)
    {
        if (!IsHunterSeason) throw new SeasonTitleValidationException("当前赛季暂不支持称号佩戴。");
        var normalized = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        lock (_gate)
        {
            Initialize();
            var key = HashAccount(account);
            var owned = ReadSeasonTitles(key);
            if (normalized is not null && !owned.Contains(normalized, StringComparer.Ordinal))
                throw new SeasonTitleValidationException("只能佩戴自己已拥有的称号。");
            using var connection = OpenSeasonTitleDatabase(_seasonTitleEquipmentDatabasePath, writable: true);
            using var transaction = connection.BeginTransaction(deferred: false);
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT title FROM rank_season_title_equipment WHERE account_key=$key;";
            read.Parameters.AddWithValue("$key", key);
            if (string.Equals(read.ExecuteScalar() as string, normalized, StringComparison.Ordinal))
            {
                transaction.Commit();
                return;
            }
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO rank_season_title_equipment VALUES($key,$title,$now)
                ON CONFLICT(account_key) DO UPDATE SET title=excluded.title,updated_at_utc=excluded.updated_at_utc;
                UPDATE rank_season_title_equipment_state SET revision=revision+1 WHERE id=1;
                """;
            write.Parameters.AddWithValue("$key", key);
            write.Parameters.AddWithValue("$title", (object?)normalized ?? DBNull.Value);
            write.Parameters.AddWithValue("$now", ChatDecorationUtcText(DateTime.UtcNow));
            write.ExecuteNonQuery();
            transaction.Commit();
        }
        RefreshSeasonTitleDisplay();
    }

    private string? ReadEquippedSeasonTitle(string key, IReadOnlyList<string> owned)
    {
        if (!IsHunterSeason || owned.Count == 0) return null;
        using var connection = OpenSeasonTitleDatabase(_seasonTitleEquipmentDatabasePath);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT title FROM rank_season_title_equipment WHERE account_key=$key;";
        read.Parameters.AddWithValue("$key", key);
        var title = read.ExecuteScalar() as string;
        return title is not null && owned.Contains(title, StringComparer.Ordinal) ? title : null;
    }

    private sealed record SeasonTitleEquipment(long Revision, IReadOnlyDictionary<string, string?> Titles);

    private SeasonTitleEquipment ReadSeasonTitleEquipment()
    {
        var titles = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!IsHunterSeason) return new(0, titles);
        using var connection = OpenSeasonTitleDatabase(_seasonTitleEquipmentDatabasePath);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT revision FROM rank_season_title_equipment_state WHERE id=1;";
        var revision = (long)read.ExecuteScalar()!;
        read.CommandText = "SELECT account_key,title FROM rank_season_title_equipment;";
        using (var reader = read.ExecuteReader())
            while (reader.Read()) titles[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        transaction.Commit();
        return new(revision, titles);
    }

    private static string? ValidEquippedTitle(string key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> owned, IReadOnlyDictionary<string, string?> equipment)
        => equipment.TryGetValue(key, out var title) && title is not null
            && owned.TryGetValue(key, out var titles) && titles.Contains(title, StringComparer.Ordinal) ? title : null;

    private void RefreshSeasonTitleDisplay()
    {
        if (!IsHunterSeason) return;
        var current = Volatile.Read(ref _publicLeaderboardSnapshot);
        if (current is null) return;
        var equipment = ReadSeasonTitleEquipment();
        if (current.SeasonTitleEquipmentRevision == equipment.Revision || !_leaderboardRefreshGate.Wait(0)) return;
        try
        {
            current = Volatile.Read(ref _publicLeaderboardSnapshot);
            if (current is null) return;
            equipment = ReadSeasonTitleEquipment();
            if (current.SeasonTitleEquipmentRevision == equipment.Revision) return;
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            var owned = ReadAllSeasonTitles(connection, transaction);
            transaction.Commit();
            var items = current.Items.Select(entry => entry with
            {
                Item = entry.Item with
                {
                    SeasonTitles = owned.GetValueOrDefault(entry.AccountKey) ?? Array.Empty<string>(),
                    EquippedSeasonTitle = ValidEquippedTitle(entry.AccountKey, owned, equipment.Titles),
                },
            }).ToArray();
            // 只更新称号展示与快照版本，保留榜单分数和生成时间，不等待十分钟重排。
            Volatile.Write(ref _publicLeaderboardSnapshot, current with
            {
                Version = ReserveNextSnapshotVersion(),
                Items = Array.AsReadOnly(items),
                SeasonTitleEquipmentRevision = equipment.Revision,
            });
        }
        finally { _leaderboardRefreshGate.Release(); }
    }
}
