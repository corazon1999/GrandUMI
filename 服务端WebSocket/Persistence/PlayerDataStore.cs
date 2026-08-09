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
    public const int MaxCardBackNameLength = 30;
    public const int MaxCardBackImageBytes = 240 * 1024;
    public const int MaxCardBacksPerPlayer = 20;
    public const int MaxCardBackGalleryItems = 100;
    public const int MaxDeckPublicationsPerPlayer = 10;
    public const int MaxDeckPlazaTitleLength = 50;
    public const int MaxDeckPlazaPageSize = 30;
    private const string CustomCardBackPrefix = "custom-";
    private const string DeckPublicationPrefix = "deck-";

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

            CREATE TABLE IF NOT EXISTS friendships (
                player_low_id  INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                player_high_id INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                created_at     INTEGER NOT NULL,
                PRIMARY KEY(player_low_id, player_high_id),
                CHECK(player_low_id < player_high_id)
            );

            CREATE TABLE IF NOT EXISTS friend_requests (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                player_low_id  INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                player_high_id INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                sender_id      INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                receiver_id    INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                created_at     INTEGER NOT NULL,
                UNIQUE(player_low_id, player_high_id),
                CHECK(player_low_id < player_high_id),
                CHECK(sender_id <> receiver_id)
            );

            CREATE INDEX IF NOT EXISTS ix_friend_requests_receiver
                ON friend_requests(receiver_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_friend_requests_sender
                ON friend_requests(sender_id, created_at DESC);

            CREATE TABLE IF NOT EXISTS card_backs (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_player_id INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                name            TEXT NOT NULL COLLATE NOCASE,
                image_mime      TEXT NOT NULL,
                image_data      BLOB NOT NULL,
                created_at      INTEGER NOT NULL,
                UNIQUE(owner_player_id, name)
            );

            CREATE TABLE IF NOT EXISTS card_back_likes (
                card_back_id INTEGER NOT NULL REFERENCES card_backs(id) ON DELETE CASCADE,
                player_id    INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                created_at   INTEGER NOT NULL,
                PRIMARY KEY(card_back_id, player_id)
            );

            CREATE INDEX IF NOT EXISTS ix_card_back_likes_card
                ON card_back_likes(card_back_id);
            CREATE INDEX IF NOT EXISTS ix_card_backs_created
                ON card_backs(created_at DESC);

            CREATE TABLE IF NOT EXISTS deck_publications (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_player_id INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                title           TEXT NOT NULL,
                leader          TEXT NOT NULL,
                leader_name     TEXT NOT NULL,
                leader_sprite   TEXT NOT NULL DEFAULT '',
                leader_color    TEXT NOT NULL,
                char_count      INTEGER NOT NULL,
                event_count     INTEGER NOT NULL,
                stage_count     INTEGER NOT NULL,
                cards_json      TEXT NOT NULL,
                sprite_map_json TEXT NOT NULL DEFAULT '{}',
                copy_count      INTEGER NOT NULL DEFAULT 0,
                created_at      INTEGER NOT NULL,
                updated_at      INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS deck_publication_likes (
                publication_id INTEGER NOT NULL REFERENCES deck_publications(id) ON DELETE CASCADE,
                player_id      INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                created_at     INTEGER NOT NULL,
                PRIMARY KEY(publication_id, player_id)
            );

            CREATE INDEX IF NOT EXISTS ix_deck_publications_updated
                ON deck_publications(updated_at DESC, id DESC);
            CREATE INDEX IF NOT EXISTS ix_deck_publications_owner
                ON deck_publications(owner_player_id, updated_at DESC);
            CREATE INDEX IF NOT EXISTS ix_deck_publication_likes_publication
                ON deck_publication_likes(publication_id);

            PRAGMA user_version=4;
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

    public FriendDataSnapshot GetFriendData(string account)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var snapshot = LoadFriendData(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public bool AreFriends(string account, string otherAccount)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var otherId = FindPlayerId(connection, transaction, NormalizeAccountKey(otherAccount));
        if (otherId is null || otherId.Value == playerId) return false;

        var (low, high) = OrderedPair(playerId, otherId.Value);
        var areFriends = FriendshipExists(connection, transaction, low, high);
        transaction.Commit();
        return areFriends;
    }

    public IReadOnlyList<FriendSearchPlayer> SearchPlayers(string account, string query, int limit = 20)
    {
        var normalizedQuery = (query ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedQuery.Length is < 1 or > MaxAccountLength)
            throw new PlayerDataValidationException($"请输入 1–{MaxAccountLength} 个字符进行搜索。");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var relationships = LoadFriendData(connection, transaction, playerId);
        var friendAccounts = relationships.Friends.Select(x => x.Account).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incomingAccounts = relationships.IncomingRequests.Select(x => x.Account).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outgoingAccounts = relationships.OutgoingRequests.Select(x => x.Account).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT account, display_name, avatar
            FROM players
            WHERE id<>$playerId
              AND (account_key LIKE $pattern ESCAPE '\' COLLATE NOCASE
                   OR display_name LIKE $pattern ESCAPE '\' COLLATE NOCASE)
            ORDER BY CASE WHEN account_key=$exact COLLATE NOCASE THEN 0 ELSE 1 END,
                     display_name COLLATE NOCASE,
                     account_key
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalizedQuery)}%");
        command.Parameters.AddWithValue("$exact", NormalizeAccountKey(normalizedQuery));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20));

        var results = new List<FriendSearchPlayer>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var resultAccount = reader.GetString(0);
            var relationship = friendAccounts.Contains(resultAccount) ? "friend"
                : incomingAccounts.Contains(resultAccount) ? "incoming"
                : outgoingAccounts.Contains(resultAccount) ? "outgoing"
                : "none";
            results.Add(new FriendSearchPlayer(
                resultAccount,
                reader.GetString(1),
                reader.GetString(2),
                relationship));
        }
        transaction.Commit();
        return results;
    }

    public FriendMutationResult SendFriendRequest(string account, string toAccount)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var targetId = FindPlayerId(connection, transaction, NormalizeAccountKey(toAccount));
        if (targetId is null) throw new PlayerDataValidationException("未找到该玩家。");
        if (playerId == targetId.Value) throw new PlayerDataValidationException("不能添加自己为好友。");

        var (low, high) = OrderedPair(playerId, targetId.Value);
        if (FriendshipExists(connection, transaction, low, high))
            throw new PlayerDataValidationException("你们已经是好友了。");

        var autoAccepted = false;
        using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = "SELECT sender_id FROM friend_requests WHERE player_low_id=$low AND player_high_id=$high;";
            inspect.Parameters.AddWithValue("$low", low);
            inspect.Parameters.AddWithValue("$high", high);
            var existingSender = inspect.ExecuteScalar();
            if (existingSender is not null)
            {
                if (Convert.ToInt64(existingSender, CultureInfo.InvariantCulture) == playerId)
                    throw new PlayerDataValidationException("好友申请已经发送，请等待对方处理。");

                DeleteFriendRequestPair(connection, transaction, low, high);
                InsertFriendship(connection, transaction, low, high);
                autoAccepted = true;
            }
        }

        if (!autoAccepted)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO friend_requests(player_low_id, player_high_id, sender_id, receiver_id, created_at)
                VALUES($low, $high, $sender, $receiver, $now);
                """;
            insert.Parameters.AddWithValue("$low", low);
            insert.Parameters.AddWithValue("$high", high);
            insert.Parameters.AddWithValue("$sender", playerId);
            insert.Parameters.AddWithValue("$receiver", targetId.Value);
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }

        var snapshot = LoadFriendData(connection, transaction, playerId);
        var otherAccount = GetAccount(connection, transaction, targetId.Value);
        transaction.Commit();
        return new FriendMutationResult(snapshot, otherAccount, autoAccepted);
    }

    public FriendMutationResult RespondFriendRequest(string account, long requestId, bool accept)
    {
        if (requestId <= 0) throw new PlayerDataValidationException("好友申请无效或已处理。");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        long senderId;
        long low;
        long high;
        using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = """
                SELECT sender_id, player_low_id, player_high_id
                FROM friend_requests WHERE id=$id AND receiver_id=$receiver;
                """;
            inspect.Parameters.AddWithValue("$id", requestId);
            inspect.Parameters.AddWithValue("$receiver", playerId);
            using var reader = inspect.ExecuteReader();
            if (!reader.Read()) throw new PlayerDataValidationException("好友申请无效或已处理。");
            senderId = reader.GetInt64(0);
            low = reader.GetInt64(1);
            high = reader.GetInt64(2);
        }

        DeleteFriendRequestPair(connection, transaction, low, high);
        if (accept) InsertFriendship(connection, transaction, low, high);

        var snapshot = LoadFriendData(connection, transaction, playerId);
        var otherAccount = GetAccount(connection, transaction, senderId);
        transaction.Commit();
        return new FriendMutationResult(snapshot, otherAccount);
    }

    public FriendMutationResult CancelFriendRequest(string account, long requestId)
    {
        if (requestId <= 0) throw new PlayerDataValidationException("好友申请无效或已处理。");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        long receiverId;
        long low;
        long high;
        using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = """
                SELECT receiver_id, player_low_id, player_high_id
                FROM friend_requests WHERE id=$id AND sender_id=$sender;
                """;
            inspect.Parameters.AddWithValue("$id", requestId);
            inspect.Parameters.AddWithValue("$sender", playerId);
            using var reader = inspect.ExecuteReader();
            if (!reader.Read()) throw new PlayerDataValidationException("好友申请无效或已处理。");
            receiverId = reader.GetInt64(0);
            low = reader.GetInt64(1);
            high = reader.GetInt64(2);
        }

        DeleteFriendRequestPair(connection, transaction, low, high);
        var snapshot = LoadFriendData(connection, transaction, playerId);
        var otherAccount = GetAccount(connection, transaction, receiverId);
        transaction.Commit();
        return new FriendMutationResult(snapshot, otherAccount);
    }

    public FriendMutationResult RemoveFriend(string account, string otherAccount)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var otherId = FindPlayerId(connection, transaction, NormalizeAccountKey(otherAccount));
        if (otherId is null || otherId.Value == playerId)
            throw new PlayerDataValidationException("好友不存在。");

        var (low, high) = OrderedPair(playerId, otherId.Value);
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM friendships WHERE player_low_id=$low AND player_high_id=$high;";
        delete.Parameters.AddWithValue("$low", low);
        delete.Parameters.AddWithValue("$high", high);
        if (delete.ExecuteNonQuery() == 0) throw new PlayerDataValidationException("好友不存在。");

        var snapshot = LoadFriendData(connection, transaction, playerId);
        var normalizedOtherAccount = GetAccount(connection, transaction, otherId.Value);
        transaction.Commit();
        return new FriendMutationResult(snapshot, normalizedOtherAccount);
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

    public DeckPlazaPage GetDeckPlaza(
        string account,
        int page = 1,
        int pageSize = 20,
        string sort = "popular",
        string? query = null,
        string? color = null,
        bool mineOnly = false)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxDeckPlazaPageSize);
        var normalizedSort = (sort ?? "popular").Trim().ToLowerInvariant();
        if (normalizedSort is not ("popular" or "newest" or "copies")) normalizedSort = "popular";
        var normalizedQuery = (query ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedQuery.Length > MaxDeckPlazaTitleLength) normalizedQuery = normalizedQuery[..MaxDeckPlazaTitleLength];
        var normalizedColor = (color ?? "").Trim();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        var conditions = new List<string>();
        if (normalizedQuery.Length > 0)
            conditions.Add("(dp.title LIKE $query ESCAPE '\\' OR p.display_name LIKE $query ESCAPE '\\')");
        if (normalizedColor.Length > 0) conditions.Add("instr(dp.leader_color, $color) > 0");
        if (mineOnly) conditions.Add("dp.owner_player_id=$playerId");
        var where = conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);

        using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = $"SELECT COUNT(*) FROM deck_publications dp JOIN players p ON p.id=dp.owner_player_id {where};";
        AddDeckPlazaFilterParameters(count, playerId, normalizedQuery, normalizedColor);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);

        var orderBy = normalizedSort switch
        {
            "newest" => "dp.updated_at DESC, dp.id DESC",
            "copies" => "dp.copy_count DESC, likes DESC, dp.updated_at DESC, dp.id DESC",
            _ => "likes DESC, dp.copy_count DESC, dp.updated_at DESC, dp.id DESC",
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT dp.id, dp.title, p.display_name,
                   dp.leader, dp.leader_name, dp.leader_sprite, dp.leader_color,
                   dp.char_count, dp.event_count, dp.stage_count,
                   dp.cards_json, dp.sprite_map_json,
                   (SELECT COUNT(*) FROM deck_publication_likes likes WHERE likes.publication_id=dp.id) AS likes,
                   EXISTS(SELECT 1 FROM deck_publication_likes mine WHERE mine.publication_id=dp.id AND mine.player_id=$playerId) AS liked,
                   CASE WHEN dp.owner_player_id=$playerId THEN 1 ELSE 0 END AS owned,
                   dp.copy_count, dp.created_at, dp.updated_at
            FROM deck_publications dp
            JOIN players p ON p.id=dp.owner_player_id
            {where}
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        AddDeckPlazaFilterParameters(command, playerId, normalizedQuery, normalizedColor);
        command.Parameters.AddWithValue("$limit", normalizedPageSize);
        command.Parameters.AddWithValue("$offset", (normalizedPage - 1) * normalizedPageSize);
        var items = new List<DeckPlazaItem>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) items.Add(ReadDeckPlazaItem(reader));

        transaction.Commit();
        return new DeckPlazaPage(items, normalizedPage, normalizedPageSize, total, normalizedPage * normalizedPageSize < total);
    }

    public string PublishDeckToPlaza(
        string account,
        string sourceDeckName,
        string title,
        string leaderColor,
        string? publicationId = null)
    {
        var normalizedTitle = ValidateDeckPlazaTitle(title);
        var normalizedColor = (leaderColor ?? "").Trim();
        if (normalizedColor.Length is < 1 or > 20 || normalizedColor.Any(char.IsControl))
            throw new PlayerDataValidationException("领航颜色无效。");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var deck = LoadStoredDeck(connection, transaction, playerId, ValidateDeckName(sourceDeckName))
                   ?? throw new PlayerDataValidationException("要发布的卡组不存在。");
        var cardsJson = JsonSerializer.Serialize(deck.Cards);
        var publicationKey = string.IsNullOrWhiteSpace(publicationId) ? (long?)null : ParseDeckPublicationId(publicationId);

        using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;
            duplicate.CommandText = """
                SELECT 1 FROM deck_publications
                WHERE owner_player_id=$playerId AND leader=$leader AND cards_json=$cards
                  AND ($publicationId IS NULL OR id<>$publicationId)
                LIMIT 1;
                """;
            duplicate.Parameters.AddWithValue("$playerId", playerId);
            duplicate.Parameters.AddWithValue("$leader", deck.Leader);
            duplicate.Parameters.AddWithValue("$cards", cardsJson);
            duplicate.Parameters.AddWithValue("$publicationId", (object?)publicationKey ?? DBNull.Value);
            if (duplicate.ExecuteScalar() is not null)
                throw new PlayerDataValidationException("这套构筑已经发布过了。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (publicationKey is null)
        {
            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM deck_publications WHERE owner_player_id=$playerId;";
            count.Parameters.AddWithValue("$playerId", playerId);
            if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) >= MaxDeckPublicationsPerPlayer)
                throw new PlayerDataValidationException($"每位玩家最多发布 {MaxDeckPublicationsPerPlayer} 副卡组。");

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO deck_publications(
                    owner_player_id, title, leader, leader_name, leader_sprite, leader_color,
                    char_count, event_count, stage_count, cards_json, sprite_map_json,
                    copy_count, created_at, updated_at)
                VALUES(
                    $playerId, $title, $leader, $leaderName, $leaderSprite, $leaderColor,
                    $charCount, $eventCount, $stageCount, $cardsJson, $spriteMapJson,
                    0, $now, $now);
                SELECT last_insert_rowid();
                """;
            AddDeckPublicationParameters(insert, playerId, normalizedTitle, normalizedColor, deck, now);
            publicationKey = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            RequireOwnedDeckPublication(connection, transaction, publicationKey.Value, playerId);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE deck_publications SET
                    title=$title, leader=$leader, leader_name=$leaderName,
                    leader_sprite=$leaderSprite, leader_color=$leaderColor,
                    char_count=$charCount, event_count=$eventCount, stage_count=$stageCount,
                    cards_json=$cardsJson, sprite_map_json=$spriteMapJson, updated_at=$now
                WHERE id=$publicationId AND owner_player_id=$playerId;
                """;
            AddDeckPublicationParameters(update, playerId, normalizedTitle, normalizedColor, deck, now);
            update.Parameters.AddWithValue("$publicationId", publicationKey.Value);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return $"{DeckPublicationPrefix}{publicationKey.Value}";
    }

    public void ToggleDeckPlazaLike(string account, string publicationId)
    {
        var publicationKey = ParseDeckPublicationId(publicationId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        RequireDeckPublication(connection, transaction, publicationKey);

        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM deck_publication_likes WHERE publication_id=$id AND player_id=$playerId;";
        remove.Parameters.AddWithValue("$id", publicationKey);
        remove.Parameters.AddWithValue("$playerId", playerId);
        if (remove.ExecuteNonQuery() == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO deck_publication_likes(publication_id, player_id, created_at)
                VALUES($id, $playerId, $now);
                """;
            insert.Parameters.AddWithValue("$id", publicationKey);
            insert.Parameters.AddWithValue("$playerId", playerId);
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public DeckPlazaCopyResult CopyDeckFromPlaza(string account, string publicationId)
    {
        var publicationKey = ParseDeckPublicationId(publicationId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        if (CountDecks(connection, transaction, playerId) >= MaxDecksPerPlayer)
            throw new PlayerDataValidationException($"每个账号最多保存 {MaxDecksPerPlayer} 副卡组。");

        var (title, deck) = LoadDeckPublicationDeck(connection, transaction, publicationKey);
        var candidate = DeckExists(connection, transaction, playerId, title)
            ? NextPlazaDeckName(connection, transaction, playerId, title)
            : title;
        var copied = ValidateDeck(deck with { Name = candidate, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        InsertDeck(connection, transaction, playerId, copied);

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE deck_publications SET copy_count=copy_count+1 WHERE id=$id;";
        update.Parameters.AddWithValue("$id", publicationKey);
        update.ExecuteNonQuery();

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return new DeckPlazaCopyResult(snapshot, candidate);
    }

    public void DeleteDeckPublication(string account, string publicationId)
    {
        var publicationKey = ParseDeckPublicationId(publicationId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        RequireOwnedDeckPublication(connection, transaction, publicationKey, playerId);
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM deck_publications WHERE id=$id AND owner_player_id=$playerId;";
        delete.Parameters.AddWithValue("$id", publicationKey);
        delete.Parameters.AddWithValue("$playerId", playerId);
        delete.ExecuteNonQuery();
        transaction.Commit();
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

    /// <summary>保存账号卡背；选用玩家投稿时会幂等地补上点赞。</summary>
    public CardBackSelectionResult UpdateCardBack(string account, string cardBackId)
    {
        var normalizedCardBackId = NormalizeCardBackReference(cardBackId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        if (TryParseCustomCardBackId(normalizedCardBackId, out var customId))
        {
            RequireCardBack(connection, transaction, customId);
            AddCardBackLike(connection, transaction, customId, playerId);
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE players SET card_back_id=$cardBackId, updated_at=$now WHERE id=$id;";
        update.Parameters.AddWithValue("$cardBackId", normalizedCardBackId);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$id", playerId);
        update.ExecuteNonQuery();

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        var gallery = LoadCardBackGallery(connection, transaction, playerId, MaxCardBackGalleryItems);
        transaction.Commit();
        return new CardBackSelectionResult(snapshot, gallery);
    }

    public static string NormalizeCardBackId(string? cardBackId)
    {
        var normalized = (cardBackId ?? "").Trim().ToLowerInvariant();
        if (!ValidCardBackIds.Contains(normalized))
            throw new PlayerDataValidationException("请选择有效的卡背。");
        return normalized;
    }

    public IReadOnlyList<CardBackGalleryItem> GetCardBackGallery(string account, int limit = MaxCardBackGalleryItems)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var gallery = LoadCardBackGallery(connection, transaction, playerId, Math.Clamp(limit, 1, MaxCardBackGalleryItems));
        transaction.Commit();
        return gallery;
    }

    public IReadOnlyList<CardBackGalleryItem> UploadCardBack(
        string account,
        string name,
        string mimeType,
        string imageBase64)
    {
        var normalizedName = ValidateCardBackName(name);
        var normalizedMime = NormalizeCardBackMime(mimeType);
        byte[] imageData;
        try { imageData = Convert.FromBase64String((imageBase64 ?? "").Trim()); }
        catch (FormatException) { throw new PlayerDataValidationException("卡背图片数据无效。"); }
        ValidateCardBackImage(normalizedMime, imageData);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM card_backs WHERE owner_player_id=$playerId;";
            count.Parameters.AddWithValue("$playerId", playerId);
            if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) >= MaxCardBacksPerPlayer)
                throw new PlayerDataValidationException($"每位玩家最多上传 {MaxCardBacksPerPlayer} 款卡背。");
        }

        try
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO card_backs(owner_player_id, name, image_mime, image_data, created_at)
                VALUES($playerId, $name, $mime, $data, $now);
                """;
            insert.Parameters.AddWithValue("$playerId", playerId);
            insert.Parameters.AddWithValue("$name", normalizedName);
            insert.Parameters.AddWithValue("$mime", normalizedMime);
            insert.Parameters.Add("$data", SqliteType.Blob).Value = imageData;
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new PlayerDataValidationException("你已经上传过同名卡背，请换一个名字。");
        }

        var gallery = LoadCardBackGallery(connection, transaction, playerId, MaxCardBackGalleryItems);
        transaction.Commit();
        return gallery;
    }

    public IReadOnlyList<CardBackGalleryItem> ToggleCardBackLike(string account, string cardBackId)
    {
        var normalized = NormalizeCardBackReference(cardBackId);
        if (!TryParseCustomCardBackId(normalized, out var customId))
            throw new PlayerDataValidationException("只能为广场中的玩家卡背点赞。");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        RequireCardBack(connection, transaction, customId);

        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM card_back_likes WHERE card_back_id=$cardBackId AND player_id=$playerId;";
        remove.Parameters.AddWithValue("$cardBackId", customId);
        remove.Parameters.AddWithValue("$playerId", playerId);
        if (remove.ExecuteNonQuery() == 0) AddCardBackLike(connection, transaction, customId, playerId);

        var gallery = LoadCardBackGallery(connection, transaction, playerId, MaxCardBackGalleryItems);
        transaction.Commit();
        return gallery;
    }

    public CardBackDeletionResult DeleteCardBack(string account, string cardBackId)
    {
        var normalized = NormalizeCardBackReference(cardBackId);
        if (!TryParseCustomCardBackId(normalized, out var customId))
            throw new PlayerDataValidationException("只能删除你在广场发布的玩家卡背。");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);

        using (var ownership = connection.CreateCommand())
        {
            ownership.Transaction = transaction;
            ownership.CommandText = "SELECT owner_player_id FROM card_backs WHERE id=$id;";
            ownership.Parameters.AddWithValue("$id", customId);
            var owner = ownership.ExecuteScalar();
            if (owner is null) throw new PlayerDataValidationException("该卡背不存在或已下架。");
            if (Convert.ToInt64(owner, CultureInfo.InvariantCulture) != playerId)
                throw new PlayerDataValidationException("只能删除自己发布的卡背。");
        }

        using (var resetSelections = connection.CreateCommand())
        {
            resetSelections.Transaction = transaction;
            resetSelections.CommandText = "UPDATE players SET card_back_id=$defaultId, updated_at=$now WHERE card_back_id=$cardBackId;";
            resetSelections.Parameters.AddWithValue("$defaultId", DefaultCardBackId);
            resetSelections.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            resetSelections.Parameters.AddWithValue("$cardBackId", normalized);
            resetSelections.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM card_backs WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", customId);
            delete.ExecuteNonQuery();
        }

        var snapshot = LoadSnapshot(connection, transaction, playerId);
        var gallery = LoadCardBackGallery(connection, transaction, playerId, MaxCardBackGalleryItems);
        transaction.Commit();
        return new CardBackDeletionResult(normalized, snapshot, gallery);
    }

    public PlayerDataSnapshot GetPlayerData(string account)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var playerId = RequirePlayerId(connection, transaction, account);
        var snapshot = LoadSnapshot(connection, transaction, playerId);
        transaction.Commit();
        return snapshot;
    }

    public CardBackImage? GetCardBackImage(long id)
    {
        if (id <= 0) return null;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT image_mime, image_data FROM card_backs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new CardBackImage(reader.GetString(0), (byte[])reader[1]) : null;
    }

    private static string NormalizeCardBackReference(string? cardBackId)
    {
        var normalized = (cardBackId ?? "").Trim().ToLowerInvariant();
        if (ValidCardBackIds.Contains(normalized) || TryParseCustomCardBackId(normalized, out _)) return normalized;
        throw new PlayerDataValidationException("请选择有效的卡背。");
    }

    private static bool TryParseCustomCardBackId(string value, out long id)
    {
        id = 0;
        return value.StartsWith(CustomCardBackPrefix, StringComparison.Ordinal)
            && long.TryParse(value.AsSpan(CustomCardBackPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }

    private static string ValidateCardBackName(string? name)
    {
        var normalized = (name ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxCardBackNameLength)
            throw new PlayerDataValidationException($"卡背名字长度需为 1–{MaxCardBackNameLength} 个字符。");
        if (normalized.Any(char.IsControl)) throw new PlayerDataValidationException("卡背名字不能包含控制字符。");
        return normalized;
    }

    private static string NormalizeCardBackMime(string? mimeType) => (mimeType ?? "").Trim().ToLowerInvariant() switch
    {
        "image/png" => "image/png",
        "image/jpeg" => "image/jpeg",
        "image/webp" => "image/webp",
        _ => throw new PlayerDataValidationException("卡背图片仅支持 PNG、JPEG 或 WebP。"),
    };

    private static void ValidateCardBackImage(string mimeType, byte[] data)
    {
        if (data.Length is < 16 or > MaxCardBackImageBytes)
            throw new PlayerDataValidationException($"卡背图片需小于 {MaxCardBackImageBytes / 1024}KB。");
        var valid = mimeType switch
        {
            "image/png" => data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
                && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A,
            "image/jpeg" => data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9,
            "image/webp" => data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };
        if (!valid) throw new PlayerDataValidationException("卡背图片内容与文件类型不匹配。");
    }

    private static void RequireCardBack(SqliteConnection connection, SqliteTransaction transaction, long cardBackId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM card_backs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", cardBackId);
        if (command.ExecuteScalar() is null) throw new PlayerDataValidationException("该卡背不存在或已下架。");
    }

    private static void AddCardBackLike(SqliteConnection connection, SqliteTransaction transaction, long cardBackId, long playerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO card_back_likes(card_back_id, player_id, created_at)
            VALUES($cardBackId, $playerId, $now);
            """;
        command.Parameters.AddWithValue("$cardBackId", cardBackId);
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<CardBackGalleryItem> LoadCardBackGallery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playerId,
        int limit)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT cb.id, cb.name, p.display_name, cb.created_at,
                   COUNT(l.player_id) AS likes,
                   MAX(CASE WHEN l.player_id=$playerId THEN 1 ELSE 0 END) AS liked,
                   CASE WHEN cb.owner_player_id=$playerId THEN 1 ELSE 0 END AS owned
            FROM card_backs cb
            JOIN players p ON p.id=cb.owner_player_id
            LEFT JOIN card_back_likes l ON l.card_back_id=cb.id
            GROUP BY cb.id, cb.name, p.display_name, cb.created_at, cb.owner_player_id
            ORDER BY likes DESC, cb.created_at DESC, cb.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$limit", limit);
        var items = new List<CardBackGalleryItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            items.Add(new CardBackGalleryItem(
                $"{CustomCardBackPrefix}{id}",
                reader.GetString(1),
                reader.GetString(2),
                $"/card-back-images/{id}",
                reader.GetInt32(4),
                reader.GetInt32(5) != 0,
                reader.GetInt32(6) != 0,
                reader.GetInt64(3)));
        }
        return items;
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

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static (long Low, long High) OrderedPair(long first, long second)
        => first < second ? (first, second) : (second, first);

    private static bool FriendshipExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long low,
        long high)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM friendships WHERE player_low_id=$low AND player_high_id=$high LIMIT 1;";
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        return command.ExecuteScalar() is not null;
    }

    private static void InsertFriendship(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long low,
        long high)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO friendships(player_low_id, player_high_id, created_at)
            VALUES($low, $high, $now);
            """;
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static void DeleteFriendRequestPair(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long low,
        long high)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM friend_requests WHERE player_low_id=$low AND player_high_id=$high;";
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        command.ExecuteNonQuery();
    }

    private static string GetAccount(SqliteConnection connection, SqliteTransaction transaction, long playerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT account FROM players WHERE id=$id;";
        command.Parameters.AddWithValue("$id", playerId);
        return command.ExecuteScalar() as string
               ?? throw new PlayerDataValidationException("玩家不存在。");
    }

    private static FriendDataSnapshot LoadFriendData(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playerId)
    {
        var friends = new List<FriendProfile>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT other.id, other.account, other.display_name, other.avatar, relation.created_at
                FROM friendships relation
                JOIN players other
                  ON other.id=CASE
                      WHEN relation.player_low_id=$playerId THEN relation.player_high_id
                      ELSE relation.player_low_id
                    END
                WHERE relation.player_low_id=$playerId OR relation.player_high_id=$playerId
                ORDER BY other.display_name COLLATE NOCASE, other.account_key;
                """;
            command.Parameters.AddWithValue("$playerId", playerId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                friends.Add(new FriendProfile(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4)));
            }
        }

        var incoming = LoadFriendRequests(connection, transaction, playerId, incoming: true);
        var outgoing = LoadFriendRequests(connection, transaction, playerId, incoming: false);
        return new FriendDataSnapshot(friends, incoming, outgoing);
    }

    private static IReadOnlyList<FriendRequestSnapshot> LoadFriendRequests(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playerId,
        bool incoming)
    {
        var requests = new List<FriendRequestSnapshot>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = incoming
            ? """
                SELECT request.id, sender.account, sender.display_name, sender.avatar, request.created_at
                FROM friend_requests request
                JOIN players sender ON sender.id=request.sender_id
                WHERE request.receiver_id=$playerId
                ORDER BY request.created_at DESC;
                """
            : """
                SELECT request.id, receiver.account, receiver.display_name, receiver.avatar, request.created_at
                FROM friend_requests request
                JOIN players receiver ON receiver.id=request.receiver_id
                WHERE request.sender_id=$playerId
                ORDER BY request.created_at DESC;
                """;
        command.Parameters.AddWithValue("$playerId", playerId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            requests.Add(new FriendRequestSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }
        return requests;
    }

    private static string ValidateDeckPlazaTitle(string? title)
    {
        var normalized = (title ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxDeckPlazaTitleLength)
            throw new PlayerDataValidationException($"广场标题长度需为 1–{MaxDeckPlazaTitleLength} 个字符。");
        if (normalized.Any(char.IsControl)) throw new PlayerDataValidationException("广场标题不能包含控制字符。");
        return normalized;
    }

    private static long ParseDeckPublicationId(string? publicationId)
    {
        var normalized = (publicationId ?? "").Trim().ToLowerInvariant();
        if (!normalized.StartsWith(DeckPublicationPrefix, StringComparison.Ordinal)
            || !long.TryParse(normalized.AsSpan(DeckPublicationPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            throw new PlayerDataValidationException("卡组投稿不存在或已下架。");
        return id;
    }

    private static void AddDeckPlazaFilterParameters(
        SqliteCommand command,
        long playerId,
        string query,
        string color)
    {
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$query", $"%{EscapeLike(query)}%");
        command.Parameters.AddWithValue("$color", color);
    }

    private static DeckPlazaItem ReadDeckPlazaItem(SqliteDataReader reader)
        => new(
            $"{DeckPublicationPrefix}{reader.GetInt64(0)}",
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            JsonSerializer.Deserialize<string[]>(reader.GetString(10)) ?? [],
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(11))
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            reader.GetInt32(12),
            reader.GetInt32(13) != 0,
            reader.GetInt32(14) != 0,
            reader.GetInt32(15),
            reader.GetInt64(16),
            reader.GetInt64(17));

    private static void AddDeckPublicationParameters(
        SqliteCommand command,
        long playerId,
        string title,
        string leaderColor,
        StoredDeck deck,
        long now)
    {
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$leader", deck.Leader);
        command.Parameters.AddWithValue("$leaderName", deck.LeaderName);
        command.Parameters.AddWithValue("$leaderSprite", deck.LeaderSprite);
        command.Parameters.AddWithValue("$leaderColor", leaderColor);
        command.Parameters.AddWithValue("$charCount", deck.CharCount);
        command.Parameters.AddWithValue("$eventCount", deck.EventCount);
        command.Parameters.AddWithValue("$stageCount", deck.StageCount);
        command.Parameters.AddWithValue("$cardsJson", JsonSerializer.Serialize(deck.Cards));
        command.Parameters.AddWithValue("$spriteMapJson", JsonSerializer.Serialize(deck.SpriteMap));
        command.Parameters.AddWithValue("$now", now);
    }

    private static void RequireDeckPublication(SqliteConnection connection, SqliteTransaction transaction, long publicationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM deck_publications WHERE id=$id;";
        command.Parameters.AddWithValue("$id", publicationId);
        if (command.ExecuteScalar() is null) throw new PlayerDataValidationException("卡组投稿不存在或已下架。");
    }

    private static void RequireOwnedDeckPublication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long publicationId,
        long playerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT owner_player_id FROM deck_publications WHERE id=$id;";
        command.Parameters.AddWithValue("$id", publicationId);
        var owner = command.ExecuteScalar();
        if (owner is null) throw new PlayerDataValidationException("卡组投稿不存在或已下架。");
        if (Convert.ToInt64(owner, CultureInfo.InvariantCulture) != playerId)
            throw new PlayerDataValidationException("只能修改自己的卡组投稿。");
    }

    private static StoredDeck? LoadStoredDeck(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playerId,
        string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name, leader, leader_name, leader_sprite,
                   char_count, event_count, stage_count,
                   cards_json, sprite_map_json, client_updated_at
            FROM decks WHERE player_id=$playerId AND name=$name COLLATE NOCASE LIMIT 1;
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new StoredDeck
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
        };
    }

    private static (string Title, StoredDeck Deck) LoadDeckPublicationDeck(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long publicationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT title, leader, leader_name, leader_sprite,
                   char_count, event_count, stage_count, cards_json, sprite_map_json
            FROM deck_publications WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", publicationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new PlayerDataValidationException("卡组投稿不存在或已下架。");
        var title = reader.GetString(0);
        return (title, new StoredDeck
        {
            Name = title,
            Leader = reader.GetString(1),
            LeaderName = reader.GetString(2),
            LeaderSprite = reader.GetString(3),
            CharCount = reader.GetInt32(4),
            EventCount = reader.GetInt32(5),
            StageCount = reader.GetInt32(6),
            Cards = JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
            SpriteMap = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8))
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private static string NextPlazaDeckName(SqliteConnection connection, SqliteTransaction transaction, long playerId, string baseName)
    {
        const string suffix = "（来自广场）";
        var maxBaseLength = Math.Max(1, MaxDeckNameLength - suffix.Length);
        var candidate = baseName[..Math.Min(baseName.Length, maxBaseLength)] + suffix;
        if (!DeckExists(connection, transaction, playerId, candidate)) return candidate;
        for (var i = 2; i <= 999; i++)
        {
            var number = i.ToString(CultureInfo.InvariantCulture);
            var allowed = Math.Max(1, MaxDeckNameLength - suffix.Length - number.Length);
            candidate = baseName[..Math.Min(baseName.Length, allowed)] + suffix + number;
            if (!DeckExists(connection, transaction, playerId, candidate)) return candidate;
        }
        throw new PlayerDataValidationException("无法为广场卡组生成唯一名称。");
    }

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
