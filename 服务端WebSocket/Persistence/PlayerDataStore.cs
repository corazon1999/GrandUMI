using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

/// <summary>
/// 玩家资料与卡组的 SQLite 持久化层。
/// 每次操作使用独立短连接，SQLite 使用 WAL 保证读写并发。
/// </summary>
public sealed class PlayerDataStore
{
    public const int MaxDecksPerPlayer = 100;
    public const string DefaultCardBackId = "classic";
    private static readonly HashSet<string> ValidCardBackIds = new(StringComparer.Ordinal)
    {
        DefaultCardBackId,
        "straw-hat",
        "marine",
        "emperor",
    };
    public const int MaxAccountLength = 32;
    public const int MaxDisplayNameLength = 32;
    public const int MaxDeckNameLength = 50;
    public const int MaxAvatarLength = 300;
    public const int MaxSpritePathLength = 500;

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly bool _deferLoginWrites;
    private readonly ConcurrentDictionary<long, long> _pendingLoginTouches = new();
    private readonly SemaphoreSlim _loginFlushGate = new(1, 1);
    private readonly Timer? _loginFlushTimer;

    public PlayerDataStore(string databasePath, bool deferLoginWrites = false)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));

        _databasePath = Path.GetFullPath(databasePath);
        _deferLoginWrites = deferLoginWrites;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = deferLoginWrites,
            DefaultTimeout = 5,
        }.ToString();
        if (_deferLoginWrites)
            _loginFlushTimer = new Timer(_ => FlushPendingLoginTouches(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public string DatabasePath => _databasePath;

    public static string ResolveDefaultPath()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_PLAYER_DB");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        var configuredDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
            return Path.GetFullPath(Path.Combine(configuredDir, "players.db"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "PlayerData", "grandumi.db");
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "PlayerData", "grandumi.db");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS players (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                account_key        TEXT NOT NULL UNIQUE,
                account            TEXT NOT NULL,
                display_name       TEXT NOT NULL,
                avatar             TEXT NOT NULL DEFAULT '',
                card_back_id       TEXT NOT NULL DEFAULT 'classic',
                selected_deck_name TEXT NULL,
                created_at         INTEGER NOT NULL,
                updated_at         INTEGER NOT NULL,
                last_login_at      INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS decks (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                player_id         INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                name              TEXT NOT NULL COLLATE NOCASE,
                leader            TEXT NOT NULL,
                leader_name       TEXT NOT NULL,
                leader_sprite     TEXT NOT NULL DEFAULT '',
                char_count        INTEGER NOT NULL,
                event_count       INTEGER NOT NULL,
                stage_count       INTEGER NOT NULL,
                cards_json        TEXT NOT NULL,
                sprite_map_json   TEXT NOT NULL DEFAULT '{}',
                client_updated_at INTEGER NOT NULL,
                updated_at        INTEGER NOT NULL,
                UNIQUE(player_id, name)
            );

            CREATE INDEX IF NOT EXISTS ix_decks_player_updated
                ON decks(player_id, updated_at DESC);

            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "players", "card_back_id", "TEXT NOT NULL DEFAULT 'classic'");
    }

    public PlayerDataSnapshot Login(string account)
    {
        var normalizedAccount = ValidateAccount(account);
        var accountKey = NormalizeAccountKey(normalizedAccount);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var playerId = FindPlayerId(connection, transaction, accountKey);
        if (playerId is null)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO players(account_key, account, display_name, avatar, created_at, updated_at, last_login_at)
                VALUES($accountKey, $account, $displayName, '', $now, $now, $now);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$accountKey", accountKey);
            insert.Parameters.AddWithValue("$account", normalizedAccount);
            insert.Parameters.AddWithValue("$displayName", normalizedAccount);
            insert.Parameters.AddWithValue("$now", now);
            playerId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            if (_deferLoginWrites)
                _pendingLoginTouches[playerId.Value] = now;
            else
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE players SET last_login_at=$now WHERE id=$id;";
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$id", playerId.Value);
                update.ExecuteNonQuery();
            }
        }

        var snapshot = LoadSnapshot(connection, transaction, playerId.Value);
        transaction.Commit();
        return snapshot;
    }

    public PlayerDataSnapshot SaveDeck(string account, StoredDeck deck)
    {
        var validated = ValidateDeck(deck);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        var exists = DeckExists(connection, transaction, playerId, validated.Name);
        if (!exists && CountDecks(connection, transaction, playerId) >= MaxDecksPerPlayer)
            throw new PlayerDataValidationException($"每个账号最多保存 {MaxDecksPerPlayer} 副卡组。");

        UpsertDeck(connection, transaction, playerId, validated);
        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public DeckImportResult ImportDecks(string account, IEnumerable<StoredDeck> decks)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var imported = 0;
        var renamed = 0;
        var skipped = 0;

        foreach (var source in decks.Take(MaxDecksPerPlayer))
        {
            StoredDeck deck;
            try { deck = ValidateDeck(source); }
            catch (PlayerDataValidationException) { skipped++; continue; }

            if (CountDecks(connection, transaction, playerId) >= MaxDecksPerPlayer)
            {
                skipped++;
                continue;
            }

            if (DeckExists(connection, transaction, playerId, deck.Name))
            {
                if (DeckContentMatches(connection, transaction, playerId, deck))
                {
                    skipped++;
                    continue;
                }

                deck = deck with { Name = NextImportedName(connection, transaction, playerId, deck.Name) };
                renamed++;
            }

            InsertDeck(connection, transaction, playerId, deck);
            imported++;
        }

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return new DeckImportResult(snapshot, imported, renamed, skipped);
    }

    public PlayerDataSnapshot DeleteDeck(string account, string name)
    {
        var deckName = ValidateDeckName(name);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM decks WHERE player_id=$playerId AND name=$name COLLATE NOCASE;";
            delete.Parameters.AddWithValue("$playerId", playerId);
            delete.Parameters.AddWithValue("$name", deckName);
            delete.ExecuteNonQuery();
        }

        using (var clearSelected = connection.CreateCommand())
        {
            clearSelected.Transaction = transaction;
            clearSelected.CommandText = """
                UPDATE players
                SET selected_deck_name=NULL, updated_at=$now
                WHERE id=$playerId AND selected_deck_name=$name COLLATE NOCASE;
                """;
            clearSelected.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            clearSelected.Parameters.AddWithValue("$playerId", playerId);
            clearSelected.Parameters.AddWithValue("$name", deckName);
            clearSelected.ExecuteNonQuery();
        }

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public PlayerDataSnapshot SelectDeck(string account, string? name)
    {
        var deckName = string.IsNullOrWhiteSpace(name) ? null : ValidateDeckName(name);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        if (deckName is not null && !DeckExists(connection, transaction, playerId, deckName))
            throw new PlayerDataValidationException("选中的卡组不存在。");

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE players SET selected_deck_name=$name, updated_at=$now WHERE id=$id;";
        update.Parameters.AddWithValue("$name", (object?)deckName ?? DBNull.Value);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$id", playerId);
        update.ExecuteNonQuery();

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public PlayerDataSnapshot UpdateProfile(string account, string displayName, string avatar)
    {
        var name = (displayName ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (name.Length is < 1 or > MaxDisplayNameLength)
            throw new PlayerDataValidationException($"昵称长度需为 1–{MaxDisplayNameLength} 个字符。");

        var normalizedAvatar = (avatar ?? "").Trim();
        if (normalizedAvatar.Length > MaxAvatarLength)
            throw new PlayerDataValidationException("头像路径过长。");
        if (normalizedAvatar.Length > 0 && !normalizedAvatar.StartsWith("/", StringComparison.Ordinal))
            throw new PlayerDataValidationException("头像必须使用站内资源路径。");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE players SET display_name=$displayName, avatar=$avatar, updated_at=$now WHERE id=$id;
            """;
        update.Parameters.AddWithValue("$displayName", name);
        update.Parameters.AddWithValue("$avatar", normalizedAvatar);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$id", playerId);
        update.ExecuteNonQuery();

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    /// <summary>保存账号卡背；只接受服务端内置 ID，禁止客户端注入任意资源路径。</summary>
    public PlayerDataSnapshot UpdateCardBack(string account, string cardBackId)
    {
        var normalizedCardBackId = NormalizeCardBackId(cardBackId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE players SET card_back_id=$cardBackId, updated_at=$now WHERE id=$id;";
        update.Parameters.AddWithValue("$cardBackId", normalizedCardBackId);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$id", playerId);
        update.ExecuteNonQuery();

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public static string NormalizeCardBackId(string? cardBackId)
    {
        var normalized = (cardBackId ?? "").Trim().ToLowerInvariant();
        if (!ValidCardBackIds.Contains(normalized))
            throw new PlayerDataValidationException("请选择有效的卡背。");
        return normalized;
    }

    public int PendingLoginWrites => _pendingLoginTouches.Count;

    /// <summary>服务退出前排空合并的最后登录时间写入。</summary>
    public void Shutdown()
    {
        _loginFlushTimer?.Dispose();
        FlushPendingLoginTouches(waitForCurrentFlush: true);
        SqliteConnection.ClearAllPools();
    }

    private void FlushPendingLoginTouches(bool waitForCurrentFlush = false)
    {
        if (_pendingLoginTouches.IsEmpty) return;
        if (waitForCurrentFlush)
            _loginFlushGate.Wait();
        else if (!_loginFlushGate.Wait(0))
            return;

        try
        {
            var pending = _pendingLoginTouches.ToArray();
            if (pending.Length == 0) return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE players SET last_login_at=$now WHERE id=$id AND last_login_at<$now;";
            var nowParameter = update.Parameters.Add("$now", SqliteType.Integer);
            var idParameter = update.Parameters.Add("$id", SqliteType.Integer);
            foreach (var item in pending)
            {
                nowParameter.Value = item.Value;
                idParameter.Value = item.Key;
                update.ExecuteNonQuery();
            }
            transaction.Commit();

            var collection = (ICollection<KeyValuePair<long, long>>)_pendingLoginTouches;
            foreach (var item in pending) collection.Remove(item);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[玩家数据] 合并最后登录时间失败：{ex.Message}");
        }
        finally
        {
            _loginFlushGate.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string ValidateAccount(string account)
    {
        var normalized = (account ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxAccountLength)
            throw new PlayerDataValidationException($"账号长度需为 1–{MaxAccountLength} 个字符。");
        if (normalized.Any(char.IsControl))
            throw new PlayerDataValidationException("账号不能包含控制字符。");
        return normalized;
    }

    private static string NormalizeAccountKey(string account)
        => ValidateAccount(account).ToUpperInvariant();

    private static string ValidateDeckName(string name)
    {
        var normalized = (name ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxDeckNameLength)
            throw new PlayerDataValidationException($"卡组名称长度需为 1–{MaxDeckNameLength} 个字符。");
        if (normalized.Any(char.IsControl))
            throw new PlayerDataValidationException("卡组名称不能包含控制字符。");
        return normalized;
    }

    private static StoredDeck ValidateDeck(StoredDeck deck)
    {
        if (deck is null) throw new PlayerDataValidationException("卡组数据为空。");
        var name = ValidateDeckName(deck.Name);
        var leader = (deck.Leader ?? "").Trim().ToUpperInvariant();
        if (leader.Length is < 3 or > 24)
            throw new PlayerDataValidationException("领航卡号无效。");
        if (deck.Cards is null || deck.Cards.Length != 50)
            throw new PlayerDataValidationException("主卡组必须恰好包含 50 张卡。");

        var cards = deck.Cards.Select(card => (card ?? "").Trim().ToUpperInvariant()).ToArray();
        if (cards.Any(card => card.Length is < 3 or > 24))
            throw new PlayerDataValidationException("卡组中存在无效卡号。");

        var spriteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (number, sprite) in deck.SpriteMap ?? new Dictionary<string, string>())
        {
            var cardNumber = (number ?? "").Trim().ToUpperInvariant();
            var path = (sprite ?? "").Trim();
            if (cardNumber.Length is < 3 or > 24 || path.Length is < 1 or > MaxSpritePathLength) continue;
            if (!path.StartsWith("/", StringComparison.Ordinal)) continue;
            spriteMap[cardNumber] = path;
            if (spriteMap.Count >= 51) break;
        }

        return deck with
        {
            Name = name,
            Leader = leader,
            LeaderName = (deck.LeaderName ?? "").Trim()[..Math.Min((deck.LeaderName ?? "").Trim().Length, 100)],
            LeaderSprite = NormalizeSpritePath(deck.LeaderSprite),
            CharCount = Math.Clamp(deck.CharCount, 0, 50),
            EventCount = Math.Clamp(deck.EventCount, 0, 50),
            StageCount = Math.Clamp(deck.StageCount, 0, 50),
            Cards = cards,
            SpriteMap = spriteMap,
            UpdatedAt = deck.UpdatedAt > 0 ? deck.UpdatedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static string NormalizeSpritePath(string? path)
    {
        var normalized = (path ?? "").Trim();
        if (normalized.Length > MaxSpritePathLength ||
            (normalized.Length > 0 && !normalized.StartsWith("/", StringComparison.Ordinal))) return "";
        return normalized;
    }

    private static long? FindPlayerId(SqliteConnection connection, SqliteTransaction transaction, string accountKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM players WHERE account_key=$accountKey;";
        command.Parameters.AddWithValue("$accountKey", accountKey);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static long RequirePlayerId(SqliteConnection connection, SqliteTransaction transaction, string account)
        => FindPlayerId(connection, transaction, NormalizeAccountKey(account))
           ?? throw new PlayerDataValidationException("玩家账号不存在，请重新登录。");

    private static int CountDecks(SqliteConnection connection, SqliteTransaction transaction, long playerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM decks WHERE player_id=$playerId;";
        command.Parameters.AddWithValue("$playerId", playerId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool DeckExists(SqliteConnection connection, SqliteTransaction transaction, long playerId, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM decks WHERE player_id=$playerId AND name=$name COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static bool DeckContentMatches(SqliteConnection connection, SqliteTransaction transaction, long playerId, StoredDeck deck)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT leader, cards_json, sprite_map_json FROM decks
            WHERE player_id=$playerId AND name=$name COLLATE NOCASE LIMIT 1;
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$name", deck.Name);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return false;
        return string.Equals(reader.GetString(0), deck.Leader, StringComparison.OrdinalIgnoreCase)
            && string.Equals(reader.GetString(1), JsonSerializer.Serialize(deck.Cards), StringComparison.Ordinal)
            && string.Equals(reader.GetString(2), JsonSerializer.Serialize(deck.SpriteMap), StringComparison.Ordinal);
    }

    private static string NextImportedName(SqliteConnection connection, SqliteTransaction transaction, long playerId, string baseName)
    {
        const string suffix = "（本地导入）";
        var maxBaseLength = Math.Max(1, MaxDeckNameLength - suffix.Length - 4);
        var trimmedBase = baseName[..Math.Min(baseName.Length, maxBaseLength)];
        var candidate = trimmedBase + suffix;
        if (!DeckExists(connection, transaction, playerId, candidate)) return candidate;

        for (var i = 2; i <= 999; i++)
        {
            var number = i.ToString(CultureInfo.InvariantCulture);
            var allowed = Math.Max(1, MaxDeckNameLength - suffix.Length - number.Length);
            candidate = baseName[..Math.Min(baseName.Length, allowed)] + suffix + number;
            if (!DeckExists(connection, transaction, playerId, candidate)) return candidate;
        }

        throw new PlayerDataValidationException("无法为导入卡组生成唯一名称。");
    }

    private static void UpsertDeck(SqliteConnection connection, SqliteTransaction transaction, long playerId, StoredDeck deck)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO decks(
                player_id, name, leader, leader_name, leader_sprite,
                char_count, event_count, stage_count, cards_json, sprite_map_json,
                client_updated_at, updated_at)
            VALUES(
                $playerId, $name, $leader, $leaderName, $leaderSprite,
                $charCount, $eventCount, $stageCount, $cardsJson, $spriteMapJson,
                $clientUpdatedAt, $updatedAt)
            ON CONFLICT(player_id, name) DO UPDATE SET
                leader=excluded.leader,
                leader_name=excluded.leader_name,
                leader_sprite=excluded.leader_sprite,
                char_count=excluded.char_count,
                event_count=excluded.event_count,
                stage_count=excluded.stage_count,
                cards_json=excluded.cards_json,
                sprite_map_json=excluded.sprite_map_json,
                client_updated_at=excluded.client_updated_at,
                updated_at=excluded.updated_at;
            """;
        AddDeckParameters(command, playerId, deck, now);
        command.ExecuteNonQuery();
    }

    private static void InsertDeck(SqliteConnection connection, SqliteTransaction transaction, long playerId, StoredDeck deck)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO decks(
                player_id, name, leader, leader_name, leader_sprite,
                char_count, event_count, stage_count, cards_json, sprite_map_json,
                client_updated_at, updated_at)
            VALUES(
                $playerId, $name, $leader, $leaderName, $leaderSprite,
                $charCount, $eventCount, $stageCount, $cardsJson, $spriteMapJson,
                $clientUpdatedAt, $updatedAt);
            """;
        AddDeckParameters(command, playerId, deck, now);
        command.ExecuteNonQuery();
    }

    private static void AddDeckParameters(SqliteCommand command, long playerId, StoredDeck deck, long serverUpdatedAt)
    {
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$name", deck.Name);
        command.Parameters.AddWithValue("$leader", deck.Leader);
        command.Parameters.AddWithValue("$leaderName", deck.LeaderName);
        command.Parameters.AddWithValue("$leaderSprite", deck.LeaderSprite);
        command.Parameters.AddWithValue("$charCount", deck.CharCount);
        command.Parameters.AddWithValue("$eventCount", deck.EventCount);
        command.Parameters.AddWithValue("$stageCount", deck.StageCount);
        command.Parameters.AddWithValue("$cardsJson", JsonSerializer.Serialize(deck.Cards));
        command.Parameters.AddWithValue("$spriteMapJson", JsonSerializer.Serialize(deck.SpriteMap));
        command.Parameters.AddWithValue("$clientUpdatedAt", deck.UpdatedAt);
        command.Parameters.AddWithValue("$updatedAt", serverUpdatedAt);
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        using var reader = inspect.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        }
        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static PlayerDataSnapshot LoadSnapshot(SqliteConnection connection, SqliteTransaction transaction, long playerId)
    {
        string account;
        string displayName;
        string avatar;
        string cardBackId;
        string? selectedDeckName;

        using (var player = connection.CreateCommand())
        {
            player.Transaction = transaction;
            player.CommandText = """
                SELECT account, display_name, avatar, card_back_id, selected_deck_name FROM players WHERE id=$id;
                """;
            player.Parameters.AddWithValue("$id", playerId);
            using var reader = player.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("玩家数据不存在。");
            account = reader.GetString(0);
            displayName = reader.GetString(1);
            avatar = reader.GetString(2);
            cardBackId = reader.IsDBNull(3) ? DefaultCardBackId : reader.GetString(3);
            selectedDeckName = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        var decks = new List<StoredDeck>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT name, leader, leader_name, leader_sprite,
                       char_count, event_count, stage_count,
                       cards_json, sprite_map_json, client_updated_at
                FROM decks WHERE player_id=$playerId
                ORDER BY updated_at DESC, id ASC;
                """;
            command.Parameters.AddWithValue("$playerId", playerId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                decks.Add(new StoredDeck
                {
                    Name = reader.GetString(0),
                    Leader = reader.GetString(1),
                    LeaderName = reader.GetString(2),
                    LeaderSprite = reader.GetString(3),
                    CharCount = reader.GetInt32(4),
                    EventCount = reader.GetInt32(5),
                    StageCount = reader.GetInt32(6),
                    Cards = JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
                    SpriteMap = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8))
                                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    UpdatedAt = reader.GetInt64(9),
                });
            }
        }

        return new PlayerDataSnapshot(account, displayName, avatar, cardBackId, selectedDeckName, decks);
    }
}
