using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public enum QqLoginAccessKind
{
    Allowed,
    NeedsBinding,
    WhitelistUninitialized,
    NotWhitelisted,
    QqAlreadyBound,
}

public sealed record QqLoginAccessResult(
    QqLoginAccessKind Kind,
    string Message,
    long? WhitelistVersion = null,
    string? MaskedQq = null)
{
    public bool Allowed => Kind == QqLoginAccessKind.Allowed;
    public bool NeedsBinding => Kind == QqLoginAccessKind.NeedsBinding;
}

public sealed record QqWhitelistStatus(
    bool Initialized,
    long Version,
    int MemberCount,
    long? ImportedAt,
    string? ImportedBy,
    int DuplicateCount,
    int AddedCount,
    int RemovedCount,
    int RemovedBoundCount);

public sealed record QqWhitelistImportPreview(int TotalCount, int UniqueCount, int DuplicateCount);

public sealed record QqWhitelistImportResult(
    long Version,
    long ImportedAt,
    int MemberCount,
    int DuplicateCount,
    int AddedCount,
    int RemovedCount,
    int RemovedBoundCount);

public sealed record QqAccountBindingStatus(
    bool Bound,
    string? MaskedQq,
    bool CurrentlyWhitelisted,
    long? BoundAt);

public sealed class QqAccessValidationException : Exception
{
    public QqAccessValidationException(string message) : base(message) { }
    public QqAccessValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class QqAccessDeniedException(string message) : Exception(message);

/// <summary>
/// QQ 群成员白名单与账号唯一绑定的服务端权威存储。
/// QQ 始终以规范化字符串持久化；导入、绑定与新对局注册共享数据库路径级读写门锁，
/// 保证“导入先完成则新局拒绝；新局先注册则视为已经进行中的对局”。
/// </summary>
public sealed partial class QqAccessStore
{
    public const int MinQqLength = 5;
    public const int MaxQqLength = 12;
    public const int MaxImportBytes = 256 * 1024;
    public const int MaxImportMembers = 10_000;
    public const string NewGameDeniedMessage = "QQ 群白名单资格无效，无法进入新对局。请确认仍在群名单内并重新登录。";

    private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> DatabaseGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ReaderWriterLockSlim _gate;
    private readonly string[] _bootstrapAdministratorAccountKeys;

    public QqAccessStore(PlayerDataStore players, IEnumerable<string>? bootstrapAdministratorAccounts = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        _databasePath = Path.GetFullPath(players.DatabasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
        _gate = DatabaseGates.GetOrAdd(_databasePath, static _ => new ReaderWriterLockSlim());
        _bootstrapAdministratorAccountKeys = (bootstrapAdministratorAccounts ?? [])
            .Select(NormalizeAccount)
            .Select(static account => account.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public void Initialize()
    {
        _gate.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS qq_whitelist_state (
                    singleton_id        INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                    version             INTEGER NOT NULL CHECK(version > 0),
                    imported_at         INTEGER NOT NULL,
                    imported_by         TEXT NOT NULL,
                    member_count        INTEGER NOT NULL CHECK(member_count > 0),
                    duplicate_count     INTEGER NOT NULL CHECK(duplicate_count >= 0),
                    added_count         INTEGER NOT NULL CHECK(added_count >= 0),
                    removed_count       INTEGER NOT NULL CHECK(removed_count >= 0),
                    removed_bound_count INTEGER NOT NULL CHECK(removed_bound_count >= 0)
                );

                CREATE TABLE IF NOT EXISTS qq_whitelist_members (
                    qq      TEXT PRIMARY KEY
                            CHECK(length(qq) BETWEEN 5 AND 12 AND qq NOT GLOB '*[^0-9]*'),
                    version INTEGER NOT NULL CHECK(version > 0)
                );

                CREATE TABLE IF NOT EXISTS player_qq_bindings (
                    player_id         INTEGER PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
                    qq                TEXT NOT NULL UNIQUE
                                      CHECK(length(qq) BETWEEN 5 AND 12 AND qq NOT GLOB '*[^0-9]*'),
                    bound_at          INTEGER NOT NULL,
                    whitelist_version INTEGER NOT NULL CHECK(whitelist_version > 0)
                );

                CREATE INDEX IF NOT EXISTS ix_player_qq_bindings_qq
                    ON player_qq_bindings(qq);

                CREATE TABLE IF NOT EXISTS qq_whitelist_import_audit (
                    version             INTEGER PRIMARY KEY,
                    imported_at         INTEGER NOT NULL,
                    imported_by         TEXT NOT NULL,
                    member_count        INTEGER NOT NULL,
                    duplicate_count     INTEGER NOT NULL,
                    added_count         INTEGER NOT NULL,
                    removed_count       INTEGER NOT NULL,
                    removed_bound_count INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS qq_bootstrap_capture_state (
                    singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                    captured_at  INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS qq_bootstrap_administrators (
                    player_id   INTEGER PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
                    captured_at INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();

            using (var captured = connection.CreateCommand())
            {
                captured.Transaction = transaction;
                captured.CommandText = "SELECT 1 FROM qq_bootstrap_capture_state WHERE singleton_id=1;";
                if (captured.ExecuteScalar() is null)
                {
                    var capturedAt = Now();
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT OR IGNORE INTO qq_bootstrap_administrators(player_id, captured_at)
                        SELECT id, $capturedAt FROM players WHERE account_key=$accountKey;
                        """;
                    insert.Parameters.AddWithValue("$capturedAt", capturedAt);
                    var accountKey = insert.Parameters.Add("$accountKey", SqliteType.Text);
                    foreach (var key in _bootstrapAdministratorAccountKeys)
                    {
                        accountKey.Value = key;
                        insert.ExecuteNonQuery();
                    }

                    using var markCaptured = connection.CreateCommand();
                    markCaptured.Transaction = transaction;
                    markCaptured.CommandText = """
                        INSERT INTO qq_bootstrap_capture_state(singleton_id, captured_at)
                        VALUES(1, $capturedAt);
                        """;
                    markCaptured.Parameters.AddWithValue("$capturedAt", capturedAt);
                    markCaptured.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }
        finally
        {
            _gate.ExitWriteLock();
        }
    }

    public static string NormalizeQq(string? qq)
    {
        var normalized = (qq ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (!QqPattern().IsMatch(normalized))
            throw new QqAccessValidationException($"QQ 号必须是 {MinQqLength}–{MaxQqLength} 位纯数字字符串。");
        return normalized;
    }

    public static QqWhitelistImportPreview PreviewImport(string json)
    {
        var parsed = ParseImport(json);
        return new QqWhitelistImportPreview(parsed.TotalCount, parsed.QqNumbers.Count, parsed.DuplicateCount);
    }

    public QqWhitelistStatus GetStatus()
    {
        _gate.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            return ReadStatus(connection, transaction: null);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    public bool IsBootstrapAdministrator(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM players p
                JOIN qq_bootstrap_administrators a ON a.player_id=p.id
                WHERE p.account_key=$accountKey;
                """;
            command.Parameters.AddWithValue("$accountKey", normalizedAccount.ToUpperInvariant());
            return command.ExecuteScalar() is not null;
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    public QqWhitelistImportResult Import(string adminAccount, string json, bool initializationOnly = false)
    {
        var normalizedAdmin = NormalizeAccount(adminAccount);
        var parsed = ParseImport(json);

        _gate.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var previousStatus = ReadStatus(connection, transaction);
            if (initializationOnly && previousStatus.Initialized)
                throw new QqAccessValidationException("首份白名单已经导入，请先按同一规则完成 QQ 绑定后再进入管理界面。");
            var previous = ReadWhitelist(connection, transaction);
            var added = parsed.QqNumbers.Except(previous, StringComparer.Ordinal).Count();
            var removed = previous.Except(parsed.QqNumbers, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var removedBoundCount = CountBoundQq(connection, transaction, removed);
            var version = previousStatus.Initialized ? checked(previousStatus.Version + 1) : 1;
            var importedAt = Now();

            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM qq_whitelist_members;";
                clear.ExecuteNonQuery();
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO qq_whitelist_members(qq, version) VALUES($qq, $version);";
                var qqParameter = insert.Parameters.Add("$qq", SqliteType.Text);
                insert.Parameters.AddWithValue("$version", version);
                foreach (var qq in parsed.QqNumbers.Order(StringComparer.Ordinal))
                {
                    qqParameter.Value = qq;
                    insert.ExecuteNonQuery();
                }
            }

            using (var state = connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = """
                    INSERT INTO qq_whitelist_state(
                        singleton_id, version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count)
                    VALUES(1, $version, $importedAt, $importedBy, $memberCount,
                        $duplicateCount, $addedCount, $removedCount, $removedBoundCount)
                    ON CONFLICT(singleton_id) DO UPDATE SET
                        version=excluded.version,
                        imported_at=excluded.imported_at,
                        imported_by=excluded.imported_by,
                        member_count=excluded.member_count,
                        duplicate_count=excluded.duplicate_count,
                        added_count=excluded.added_count,
                        removed_count=excluded.removed_count,
                        removed_bound_count=excluded.removed_bound_count;
                    """;
                AddImportParameters(
                    state,
                    version,
                    importedAt,
                    normalizedAdmin,
                    parsed.QqNumbers.Count,
                    parsed.DuplicateCount,
                    added,
                    removed.Count,
                    removedBoundCount);
                state.ExecuteNonQuery();
            }

            using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = """
                    INSERT INTO qq_whitelist_import_audit(
                        version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count)
                    VALUES($version, $importedAt, $importedBy, $memberCount,
                        $duplicateCount, $addedCount, $removedCount, $removedBoundCount);
                    """;
                AddImportParameters(
                    audit,
                    version,
                    importedAt,
                    normalizedAdmin,
                    parsed.QqNumbers.Count,
                    parsed.DuplicateCount,
                    added,
                    removed.Count,
                    removedBoundCount);
                audit.ExecuteNonQuery();
            }

            transaction.Commit();
            return new QqWhitelistImportResult(
                version,
                importedAt,
                parsed.QqNumbers.Count,
                parsed.DuplicateCount,
                added,
                removed.Count,
                removedBoundCount);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessValidationException("白名单数据库正忙，请稍后重试。", ex);
        }
        finally
        {
            _gate.ExitWriteLock();
        }
    }

    /// <summary>凭证认证通过后调用；只有此方法返回 Allowed 才能建立完整业务会话。</summary>
    public QqLoginAccessResult EvaluateLogin(string account, string? submittedQq)
    {
        var normalizedAccount = NormalizeAccount(account);
        var normalizedQq = submittedQq is null ? null : NormalizeQq(submittedQq);

        _gate.EnterUpgradeableReadLock();
        try
        {
            using (var connection = OpenConnection())
            {
                var status = ReadStatus(connection, transaction: null);
                if (!status.Initialized)
                    return new QqLoginAccessResult(
                        QqLoginAccessKind.WhitelistUninitialized,
                        "QQ 群白名单尚未初始化，请联系管理员。");

                var binding = ReadBinding(connection, transaction: null, normalizedAccount);
                if (binding is not null)
                {
                    if (normalizedQq is not null && !string.Equals(binding.Value.Qq, normalizedQq, StringComparison.Ordinal))
                        return new QqLoginAccessResult(
                            QqLoginAccessKind.QqAlreadyBound,
                            "该账号已经绑定 QQ，玩家不能自行更换绑定。",
                            status.Version,
                            MaskQq(binding.Value.Qq));
                    return IsWhitelisted(connection, transaction: null, binding.Value.Qq)
                        ? new QqLoginAccessResult(
                            QqLoginAccessKind.Allowed,
                            "QQ 群白名单验证通过。",
                            status.Version,
                            MaskQq(binding.Value.Qq))
                        : new QqLoginAccessResult(
                            QqLoginAccessKind.NotWhitelisted,
                            "绑定 QQ 当前不在群白名单内，无法登录。",
                            status.Version,
                            MaskQq(binding.Value.Qq));
                }

                if (normalizedQq is null)
                    return new QqLoginAccessResult(
                        QqLoginAccessKind.NeedsBinding,
                        "首次登录需要绑定当前群白名单内的 QQ。绑定后玩家不能自行更换。",
                        status.Version);
            }

            _gate.EnterWriteLock();
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var status = ReadStatus(connection, transaction);
                if (!status.Initialized)
                    return new QqLoginAccessResult(
                        QqLoginAccessKind.WhitelistUninitialized,
                        "QQ 群白名单尚未初始化，请联系管理员。");

                var existing = ReadBinding(connection, transaction, normalizedAccount);
                if (existing is not null)
                {
                    transaction.Commit();
                    if (!string.Equals(existing.Value.Qq, normalizedQq, StringComparison.Ordinal))
                        return new QqLoginAccessResult(
                            QqLoginAccessKind.QqAlreadyBound,
                            "该账号已经绑定 QQ，玩家不能自行更换绑定。",
                            status.Version,
                            MaskQq(existing.Value.Qq));
                    return IsWhitelisted(connection, transaction: null, existing.Value.Qq)
                        ? new QqLoginAccessResult(
                            QqLoginAccessKind.Allowed,
                            "QQ 绑定已存在，群白名单验证通过。",
                            status.Version,
                            MaskQq(existing.Value.Qq))
                        : new QqLoginAccessResult(
                            QqLoginAccessKind.NotWhitelisted,
                            "绑定 QQ 当前不在群白名单内，无法登录。",
                            status.Version,
                            MaskQq(existing.Value.Qq));
                }

                if (!IsWhitelisted(connection, transaction, normalizedQq!))
                    return new QqLoginAccessResult(
                        QqLoginAccessKind.NotWhitelisted,
                        "该 QQ 不在当前群白名单内，无法绑定。",
                        status.Version);

                var playerId = FindPlayerId(connection, transaction, normalizedAccount);
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO player_qq_bindings(player_id, qq, bound_at, whitelist_version)
                    VALUES($playerId, $qq, $boundAt, $version);
                    """;
                insert.Parameters.AddWithValue("$playerId", playerId);
                insert.Parameters.AddWithValue("$qq", normalizedQq!);
                insert.Parameters.AddWithValue("$boundAt", Now());
                insert.Parameters.AddWithValue("$version", status.Version);
                try
                {
                    insert.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    transaction.Rollback();
                    return new QqLoginAccessResult(
                        QqLoginAccessKind.QqAlreadyBound,
                        "该 QQ 已绑定其他账号，一个 QQ 只能绑定一个账号。",
                        status.Version);
                }
                transaction.Commit();
                return new QqLoginAccessResult(
                    QqLoginAccessKind.Allowed,
                    "QQ 绑定成功，群白名单验证通过。",
                    status.Version,
                    MaskQq(normalizedQq!));
            }
            finally
            {
                _gate.ExitWriteLock();
            }
        }
        finally
        {
            _gate.ExitUpgradeableReadLock();
        }
    }

    public QqLoginAccessResult CheckNewGameAccess(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            return CheckNewGameAccess(connection, normalizedAccount);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    /// <summary>
    /// 在共享读锁内完成最终资格复核和新房间权威注册；导入必须等待注册完成，
    /// 因而不存在“复核通过后、房间创建前被移出名单”的未定义窗口。
    /// </summary>
    public T ExecuteNewGameAdmission<T>(IEnumerable<string?> accounts, Func<T> registerGame)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registerGame);
        var normalizedAccounts = accounts
            .Where(static account => !string.IsNullOrWhiteSpace(account))
            .Select(account => NormalizeAccount(account!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedAccounts.Length == 0)
            throw new QqAccessDeniedException(NewGameDeniedMessage);

        _gate.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            foreach (var account in normalizedAccounts)
            {
                var access = CheckNewGameAccess(connection, account);
                if (!access.Allowed) throw new QqAccessDeniedException(NewGameDeniedMessage);
            }
            return registerGame();
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    public QqAccountBindingStatus GetAccountBindingStatus(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            var binding = ReadBinding(connection, transaction: null, normalizedAccount);
            return binding is null
                ? new QqAccountBindingStatus(false, null, false, null)
                : new QqAccountBindingStatus(
                    true,
                    MaskQq(binding.Value.Qq),
                    IsWhitelisted(connection, transaction: null, binding.Value.Qq),
                    binding.Value.BoundAt);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    private QqLoginAccessResult CheckNewGameAccess(SqliteConnection connection, string normalizedAccount)
    {
        var status = ReadStatus(connection, transaction: null);
        if (!status.Initialized)
            return new QqLoginAccessResult(QqLoginAccessKind.WhitelistUninitialized, NewGameDeniedMessage);
        var binding = ReadBinding(connection, transaction: null, normalizedAccount);
        if (binding is null)
            return new QqLoginAccessResult(QqLoginAccessKind.NeedsBinding, NewGameDeniedMessage, status.Version);
        return IsWhitelisted(connection, transaction: null, binding.Value.Qq)
            ? new QqLoginAccessResult(QqLoginAccessKind.Allowed, "QQ 群白名单验证通过。", status.Version, MaskQq(binding.Value.Qq))
            : new QqLoginAccessResult(QqLoginAccessKind.NotWhitelisted, NewGameDeniedMessage, status.Version, MaskQq(binding.Value.Qq));
    }

    private static ParsedImport ParseImport(string json)
    {
        if (json is null) throw new QqAccessValidationException("请选择有效的 JSON 文件。");
        if (Encoding.UTF8.GetByteCount(json) > MaxImportBytes)
            throw new QqAccessValidationException($"JSON 文件不能超过 {MaxImportBytes / 1024} KiB。");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var array = ResolveMemberArray(document.RootElement);
            if (array.GetArrayLength() == 0)
                throw new QqAccessValidationException("拒绝导入空白名单，以免意外锁定全部账号。");
            if (array.GetArrayLength() > MaxImportMembers)
                throw new QqAccessValidationException($"群成员条目不能超过 {MaxImportMembers} 条。");

            var values = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            foreach (var item in array.EnumerateArray())
            {
                total++;
                values.Add(ParseQqValue(ResolveQqValue(item, total), total));
            }
            return new ParsedImport(values, total, total - values.Count);
        }
        catch (JsonException ex)
        {
            throw new QqAccessValidationException("JSON 格式无效，请检查文件内容。", ex);
        }
    }

    private static JsonElement ResolveMemberArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object)
            throw new QqAccessValidationException("JSON 顶层必须是成员数组，或包含 members、data、list 数组。");

        foreach (var propertyName in new[] { "members", "data", "list" })
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind != JsonValueKind.Array)
                    throw new QqAccessValidationException($"字段 {property.Name} 必须是数组。");
                return property.Value;
            }
        }
        throw new QqAccessValidationException("JSON 对象缺少 members、data 或 list 成员数组。");
    }

    private static JsonElement ResolveQqValue(JsonElement item, int index)
    {
        if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number) return item;
        if (item.ValueKind != JsonValueKind.Object)
            throw new QqAccessValidationException($"第 {index} 条成员不是 QQ 字符串、数字或对象。");

        foreach (var fieldName in new[] { "qq", "uin", "user_id" })
        {
            foreach (var property in item.EnumerateObject())
                if (string.Equals(property.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
        }
        throw new QqAccessValidationException($"第 {index} 条成员对象缺少 qq、uin 或 user_id 字段。");
    }

    private static string ParseQqValue(JsonElement value, int index)
    {
        string candidate;
        if (value.ValueKind == JsonValueKind.String)
        {
            candidate = value.GetString() ?? "";
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            candidate = value.GetRawText();
            if (!candidate.All(static character => character is >= '0' and <= '9'))
                throw new QqAccessValidationException($"第 {index} 条 QQ 数字必须是无小数、无指数的正整数。");
        }
        else
        {
            throw new QqAccessValidationException($"第 {index} 条 QQ 字段必须是字符串或数字。");
        }

        try { return NormalizeQq(candidate); }
        catch (QqAccessValidationException ex)
        {
            throw new QqAccessValidationException($"第 {index} 条成员无效：{ex.Message}");
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

    private static QqWhitelistStatus ReadStatus(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT version, member_count, imported_at, imported_by,
                   duplicate_count, added_count, removed_count, removed_bound_count
            FROM qq_whitelist_state WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new QqWhitelistStatus(false, 0, 0, null, null, 0, 0, 0, 0);
        return new QqWhitelistStatus(
            true,
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    private static HashSet<string> ReadWhitelist(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT qq FROM qq_whitelist_members;";
        using var reader = command.ExecuteReader();
        var values = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    private static int CountBoundQq(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlySet<string> removed)
    {
        if (removed.Count == 0) return 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT qq FROM player_qq_bindings;";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read())
            if (removed.Contains(reader.GetString(0))) count++;
        return count;
    }

    private static (string Qq, long BoundAt)? ReadBinding(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.qq, b.bound_at
            FROM players p
            JOIN player_qq_bindings b ON b.player_id=p.id
            WHERE p.account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$accountKey", account.ToUpperInvariant());
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetInt64(1)) : null;
    }

    private static bool IsWhitelisted(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string qq)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM qq_whitelist_members WHERE qq=$qq;";
        command.Parameters.AddWithValue("$qq", qq);
        return command.ExecuteScalar() is not null;
    }

    private static long FindPlayerId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM players WHERE account_key=$accountKey;";
        command.Parameters.AddWithValue("$accountKey", account.ToUpperInvariant());
        return command.ExecuteScalar() is long playerId
            ? playerId
            : throw new QqAccessValidationException("账号认证资料不存在，请重新登录。");
    }

    private static void AddImportParameters(
        SqliteCommand command,
        long version,
        long importedAt,
        string importedBy,
        int memberCount,
        int duplicateCount,
        int addedCount,
        int removedCount,
        int removedBoundCount)
    {
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$importedAt", importedAt);
        command.Parameters.AddWithValue("$importedBy", importedBy);
        command.Parameters.AddWithValue("$memberCount", memberCount);
        command.Parameters.AddWithValue("$duplicateCount", duplicateCount);
        command.Parameters.AddWithValue("$addedCount", addedCount);
        command.Parameters.AddWithValue("$removedCount", removedCount);
        command.Parameters.AddWithValue("$removedBoundCount", removedBoundCount);
    }

    private static string NormalizeAccount(string account)
    {
        var normalized = (account ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > PlayerDataStore.MaxAccountLength || normalized.Any(char.IsControl))
            throw new QqAccessValidationException("账号格式无效。");
        return normalized;
    }

    private static string MaskQq(string qq)
    {
        if (qq.Length <= 5) return $"{qq[..1]}***{qq[^1..]}";
        return $"{qq[..3]}{new string('*', Math.Min(6, qq.Length - 5))}{qq[^2..]}";
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed record ParsedImport(HashSet<string> QqNumbers, int TotalCount, int DuplicateCount);

    [GeneratedRegex("^[0-9]{5,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex QqPattern();
}
