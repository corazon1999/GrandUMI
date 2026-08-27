using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public sealed record LegacyAccountSource(string Role, string DatabasePath, bool Authoritative);

public sealed record SharedAccountMigrationSummary(
    int SourceCount,
    int AccountCount,
    int CredentialCount,
    int SessionCount,
    int BindingCount,
    int BindingConflictCount,
    bool WhitelistInitialized,
    long WhitelistVersion);

/// <summary>
/// 测试服与正式服共同使用的账号安全数据库。玩法资料仍留在各环境自己的 players.db；
/// 此库只承载账号目录、密码会话、QQ 白名单/绑定和安全审计。
/// </summary>
public sealed class SharedAccountDatabase
{
    public const int SchemaVersion = 1;

    private readonly string _connectionString;

    public SharedAccountDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("共享账号数据库路径不能为空。", nameof(databasePath));
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 15,
        }.ToString();
    }

    public string DatabasePath { get; }

    public static string ResolveDefaultPath(string playerDatabasePath)
    {
        var localPath = Path.GetFullPath(playerDatabasePath);
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB");
        if (string.IsNullOrWhiteSpace(configured)) return localPath;

        var activationMarker = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER");
        if (!string.IsNullOrWhiteSpace(activationMarker) && !File.Exists(activationMarker))
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_ALLOW_LOCAL_FALLBACK"),
                    "1",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "共享账号库激活标记不存在，且当前服务未获准使用测试服预激活回退，拒绝启动。");
            return localPath;
        }

        var preparedMarker = Environment.GetEnvironmentVariable("GRANDUMI_ACCOUNT_DB_PREPARED_MARKER");
        if (string.IsNullOrWhiteSpace(preparedMarker))
            throw new InvalidOperationException(
                "启用独立共享账号库时必须配置 GRANDUMI_ACCOUNT_DB_PREPARED_MARKER，拒绝绕过停写迁移门禁。");
        if (!File.Exists(preparedMarker))
            throw new InvalidOperationException("共享账号库尚未完成受控迁移，准备标记不存在，拒绝启动。");

        var sharedPath = Path.GetFullPath(configured);
        if (!File.Exists(sharedPath) || new FileInfo(sharedPath).Length == 0)
            throw new InvalidOperationException("共享账号库不存在或为空，拒绝在服务启动时自动创建。");
        return sharedPath;
    }

    public SharedAccountMigrationSummary Initialize(
        IEnumerable<LegacyAccountSource>? legacySources = null,
        IEnumerable<string>? bootstrapAdministratorAccounts = null,
        bool requirePreparedMigration = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=15000;";
            pragma.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction(deferred: false);
        if (requirePreparedMigration)
        {
            using var version = connection.CreateCommand();
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version;";
            var actualVersion = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (actualVersion != SchemaVersion)
                throw new InvalidOperationException(
                    $"共享账号库版本未经受控迁移：期望 {SchemaVersion}，实际 {actualVersion}，拒绝启动。");
        }
        CreateSchema(connection, transaction);
        if (requirePreparedMigration && !HasCompletedLegacyMigration(connection, transaction))
            throw new InvalidOperationException("共享账号库缺少已完成的源数据迁移审计，拒绝启动。");

        var sources = (legacySources ?? [])
            .Where(source => !string.IsNullOrWhiteSpace(source.DatabasePath) && File.Exists(source.DatabasePath))
            .Select(source => source with { DatabasePath = Path.GetFullPath(source.DatabasePath) })
            .GroupBy(source => source.DatabasePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(source => source.Authoritative).First())
            .OrderByDescending(source => source.Authoritative)
            .ThenBy(source => source.Role, StringComparer.Ordinal)
            .ToArray();

        var bindingConflicts = 0;
        foreach (var source in sources)
            bindingConflicts += ImportLegacySource(connection, transaction, source);

        CaptureBootstrapAdministrators(connection, transaction, bootstrapAdministratorAccounts ?? []);
        var summary = ReadSummary(connection, transaction, sources.Length, bindingConflicts);
        if (sources.Length > 0)
        {
            using var audit = connection.CreateCommand();
            audit.Transaction = transaction;
            audit.CommandText = """
                    INSERT INTO shared_account_migration_audit(
                        schema_version, source_count, account_count, credential_count,
                        session_count, binding_count, binding_conflict_count,
                        whitelist_initialized, whitelist_version, created_at)
                    VALUES($schemaVersion, $sourceCount, $accountCount, $credentialCount,
                        $sessionCount, $bindingCount, $bindingConflictCount,
                        $whitelistInitialized, $whitelistVersion, $createdAt);
                    """;
            audit.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
            audit.Parameters.AddWithValue("$sourceCount", summary.SourceCount);
            audit.Parameters.AddWithValue("$accountCount", summary.AccountCount);
            audit.Parameters.AddWithValue("$credentialCount", summary.CredentialCount);
            audit.Parameters.AddWithValue("$sessionCount", summary.SessionCount);
            audit.Parameters.AddWithValue("$bindingCount", summary.BindingCount);
            audit.Parameters.AddWithValue("$bindingConflictCount", summary.BindingConflictCount);
            audit.Parameters.AddWithValue("$whitelistInitialized", summary.WhitelistInitialized ? 1 : 0);
            audit.Parameters.AddWithValue("$whitelistVersion", summary.WhitelistVersion);
            audit.Parameters.AddWithValue("$createdAt", Now());
            audit.ExecuteNonQuery();
        }
        transaction.Commit();

        using var verify = OpenConnection();
        using var check = verify.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
            throw new InvalidOperationException($"共享账号数据库完整性检查失败：{result ?? "未知错误"}");
        return summary;
    }

    private static bool HasCompletedLegacyMigration(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM shared_account_migration_audit
                WHERE schema_version=$schemaVersion AND source_count > 0);
            """;
        command.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    internal SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS shared_accounts (
                account_key   TEXT PRIMARY KEY,
                account       TEXT NOT NULL,
                display_name  TEXT NOT NULL,
                created_at    INTEGER NOT NULL,
                updated_at    INTEGER NOT NULL,
                last_login_at INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_shared_accounts_account_nocase
                ON shared_accounts(account COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_shared_accounts_display_name_nocase
                ON shared_accounts(display_name COLLATE NOCASE, account_key);
            CREATE INDEX IF NOT EXISTS ix_shared_accounts_last_login
                ON shared_accounts(last_login_at DESC);

            CREATE TABLE IF NOT EXISTS shared_player_credentials (
                account_key  TEXT PRIMARY KEY REFERENCES shared_accounts(account_key) ON DELETE CASCADE,
                password_hash TEXT NOT NULL,
                created_at    INTEGER NOT NULL,
                updated_at    INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shared_player_auth_sessions (
                token_hash TEXT PRIMARY KEY,
                account_key TEXT NOT NULL REFERENCES shared_accounts(account_key) ON DELETE CASCADE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_shared_auth_sessions_account
                ON shared_player_auth_sessions(account_key, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_shared_auth_sessions_expiry
                ON shared_player_auth_sessions(expires_at);

            CREATE TABLE IF NOT EXISTS shared_qq_whitelist_state (
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

            CREATE TABLE IF NOT EXISTS shared_qq_whitelist_members (
                qq      TEXT PRIMARY KEY
                        CHECK(length(qq) BETWEEN 5 AND 12 AND qq NOT GLOB '*[^0-9]*'),
                version INTEGER NOT NULL CHECK(version > 0)
            );

            CREATE TABLE IF NOT EXISTS shared_account_qq_bindings (
                account_key       TEXT PRIMARY KEY REFERENCES shared_accounts(account_key) ON DELETE CASCADE,
                qq                TEXT NULL
                                  CHECK(qq IS NULL OR (length(qq) BETWEEN 5 AND 12 AND qq NOT GLOB '*[^0-9]*')),
                revision          INTEGER NOT NULL CHECK(revision >= 1),
                bound_at          INTEGER NULL,
                whitelist_version INTEGER NULL,
                updated_at        INTEGER NOT NULL,
                updated_by        TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_shared_account_qq_bindings_qq
                ON shared_account_qq_bindings(qq) WHERE qq IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_shared_account_qq_bindings_updated
                ON shared_account_qq_bindings(updated_at DESC);

            CREATE TABLE IF NOT EXISTS shared_qq_whitelist_import_audit (
                version             INTEGER PRIMARY KEY,
                imported_at         INTEGER NOT NULL,
                imported_by         TEXT NOT NULL,
                member_count        INTEGER NOT NULL,
                duplicate_count     INTEGER NOT NULL,
                added_count         INTEGER NOT NULL,
                removed_count       INTEGER NOT NULL,
                removed_bound_count INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shared_qq_whitelist_sync_runs (
                operation_key          TEXT PRIMARY KEY
                                       CHECK(length(operation_key) BETWEEN 1 AND 128),
                group_id               TEXT NOT NULL
                                       CHECK(length(group_id) BETWEEN 5 AND 12
                                             AND group_id NOT GLOB '*[^0-9]*'),
                group_name             TEXT NOT NULL CHECK(length(group_name) BETWEEN 1 AND 100),
                scheduled_hour         INTEGER NOT NULL,
                request_hash           TEXT NOT NULL
                                       CHECK(length(request_hash) = 64
                                             AND request_hash NOT GLOB '*[^0-9A-F]*'),
                client_instance_id     TEXT NOT NULL CHECK(length(client_instance_id) = 36),
                version                INTEGER NOT NULL UNIQUE
                                       REFERENCES shared_qq_whitelist_import_audit(version),
                imported_at            INTEGER NOT NULL,
                member_count           INTEGER NOT NULL CHECK(member_count > 0),
                duplicate_count        INTEGER NOT NULL CHECK(duplicate_count = 0),
                added_count            INTEGER NOT NULL CHECK(added_count >= 0),
                removed_count          INTEGER NOT NULL CHECK(removed_count >= 0),
                removed_bound_count    INTEGER NOT NULL CHECK(removed_bound_count >= 0),
                notification_acked_at  INTEGER NULL,
                UNIQUE(group_id, scheduled_hour)
            );

            CREATE INDEX IF NOT EXISTS ix_shared_qq_sync_scheduled
                ON shared_qq_whitelist_sync_runs(scheduled_hour DESC, group_id);

            CREATE TABLE IF NOT EXISTS shared_qq_whitelist_update_events (
                id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                event_key             TEXT NOT NULL UNIQUE
                                        CHECK(length(event_key) BETWEEN 1 AND 256),
                outcome               TEXT NOT NULL
                                        CHECK(outcome IN ('success', 'failure')),
                source                TEXT NOT NULL CHECK(length(source) BETWEEN 1 AND 200),
                operation_key         TEXT NULL CHECK(
                                        operation_key IS NULL
                                        OR length(operation_key) BETWEEN 1 AND 128),
                occurred_at           INTEGER NOT NULL,
                scheduled_hour        INTEGER NULL,
                version               INTEGER NULL CHECK(version IS NULL OR version > 0),
                member_count          INTEGER NULL CHECK(member_count IS NULL OR member_count > 0),
                added_count           INTEGER NULL CHECK(added_count IS NULL OR added_count >= 0),
                removed_count         INTEGER NULL CHECK(removed_count IS NULL OR removed_count >= 0),
                removed_bound_count   INTEGER NULL CHECK(
                                        removed_bound_count IS NULL OR removed_bound_count >= 0),
                error                 TEXT NULL CHECK(error IS NULL OR length(error) BETWEEN 1 AND 1000),
                CHECK(
                    (outcome='success' AND version IS NOT NULL AND member_count IS NOT NULL AND error IS NULL)
                    OR outcome='failure')
            );

            CREATE INDEX IF NOT EXISTS ix_shared_qq_update_events_occurred
                ON shared_qq_whitelist_update_events(occurred_at DESC, id DESC);

            CREATE TABLE IF NOT EXISTS shared_qq_bootstrap_capture_state (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                captured_at  INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shared_qq_bootstrap_administrators (
                account_key TEXT PRIMARY KEY REFERENCES shared_accounts(account_key) ON DELETE CASCADE,
                captured_at INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shared_admin_player_audit (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                admin_account  TEXT NOT NULL,
                target_account TEXT NOT NULL,
                action         TEXT NOT NULL,
                request_id     TEXT NULL,
                detail_json    TEXT NOT NULL DEFAULT '{}',
                created_at     INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_shared_admin_audit_target_created
                ON shared_admin_player_audit(target_account, created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_shared_admin_audit_request
                ON shared_admin_player_audit(admin_account, request_id) WHERE request_id IS NOT NULL;

            CREATE TABLE IF NOT EXISTS shared_qq_binding_requests (
                admin_account TEXT NOT NULL,
                request_id    TEXT NOT NULL,
                payload_hash  TEXT NOT NULL,
                target_account TEXT NOT NULL,
                action         TEXT NOT NULL,
                resulting_revision INTEGER NOT NULL,
                resulting_qq_masked TEXT NULL,
                resulting_whitelisted INTEGER NOT NULL,
                created_at     INTEGER NOT NULL,
                PRIMARY KEY(admin_account, request_id)
            );

            CREATE TABLE IF NOT EXISTS shared_account_security_events (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type     TEXT NOT NULL,
                target_account TEXT NULL,
                revision       INTEGER NULL,
                created_at     INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_shared_security_events_created
                ON shared_account_security_events(id, created_at);

            CREATE TABLE IF NOT EXISTS shared_account_migration_audit (
                id                     INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_version         INTEGER NOT NULL,
                source_count           INTEGER NOT NULL,
                account_count          INTEGER NOT NULL,
                credential_count       INTEGER NOT NULL,
                session_count          INTEGER NOT NULL,
                binding_count          INTEGER NOT NULL,
                binding_conflict_count INTEGER NOT NULL,
                whitelist_initialized  INTEGER NOT NULL,
                whitelist_version      INTEGER NOT NULL,
                created_at             INTEGER NOT NULL
            );

            DELETE FROM shared_player_auth_sessions WHERE expires_at <= unixepoch('now') * 1000;
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }

    private static int ImportLegacySource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LegacyAccountSource source)
    {
        var targetPath = Path.GetFullPath(connection.DataSource);
        var sourcePath = Path.GetFullPath(source.DatabasePath);
        var schema = string.Equals(targetPath, sourcePath, StringComparison.OrdinalIgnoreCase)
            ? "main"
            : $"legacy_{Guid.NewGuid():N}";
        var attached = schema != "main";
        if (attached)
        {
            using var attach = connection.CreateCommand();
            attach.Transaction = transaction;
            attach.CommandText = $"ATTACH DATABASE $path AS {schema};";
            attach.Parameters.AddWithValue("$path", sourcePath);
            attach.ExecuteNonQuery();
        }

        // 新版测试服在正式激活前会把账号安全数据写入本地 players.db 内的
        // shared_* 表。正式切换必须优先迁移这份已物化权威视图，不能退回读取
        // 同库中已经停止更新的旧凭据/绑定表，否则预激活期间的新密码会丢失。
        if (attached
            && TableExists(connection, transaction, schema, "shared_accounts")
            && ScalarInt(connection, transaction,
                $"SELECT COUNT(*) FROM {schema}.shared_accounts;") > 0)
            return ImportMaterializedSharedSource(connection, transaction, schema, source);

        // 附加库在当前事务内只读使用。SQLite 不允许在尚未提交的事务中
        // DETACH 已读取的库；连接释放时会自动解除附加，因此在提交前保留附加。
        {
            if (!TableExists(connection, transaction, schema, "players")) return 0;

            using (var accounts = connection.CreateCommand())
            {
                accounts.Transaction = transaction;
                accounts.CommandText = $"""
                    INSERT OR IGNORE INTO shared_accounts(
                        account_key, account, display_name, created_at, updated_at, last_login_at)
                    SELECT account_key, account, display_name, created_at, updated_at, last_login_at
                    FROM {schema}.players;

                    UPDATE shared_accounts
                    SET last_login_at = max(last_login_at, COALESCE((
                        SELECT source.last_login_at FROM {schema}.players source
                        WHERE source.account_key=shared_accounts.account_key), last_login_at));
                    """;
                accounts.ExecuteNonQuery();
            }

            if (TableExists(connection, transaction, schema, "player_credentials"))
            {
                using var credentials = connection.CreateCommand();
                credentials.Transaction = transaction;
                credentials.CommandText = $"""
                    INSERT OR IGNORE INTO shared_player_credentials(
                        account_key, password_hash, created_at, updated_at)
                    SELECT p.account_key, c.password_hash, c.created_at, c.updated_at
                    FROM {schema}.player_credentials c
                    JOIN {schema}.players p ON p.id=c.player_id;
                    """;
                credentials.ExecuteNonQuery();
            }

            if (TableExists(connection, transaction, schema, "player_auth_sessions")
                && TableExists(connection, transaction, schema, "player_credentials"))
            {
                using var sessions = connection.CreateCommand();
                sessions.Transaction = transaction;
                sessions.CommandText = $"""
                    INSERT OR IGNORE INTO shared_player_auth_sessions(
                        token_hash, account_key, created_at, expires_at)
                    SELECT s.token_hash, p.account_key, s.created_at, s.expires_at
                    FROM {schema}.player_auth_sessions s
                    JOIN {schema}.players p ON p.id=s.player_id
                    WHERE s.expires_at>$now
                      AND ($authoritative=1 OR EXISTS(
                          SELECT 1
                          FROM {schema}.player_credentials source_credential
                          JOIN shared_player_credentials shared_credential
                            ON shared_credential.account_key=p.account_key
                           AND shared_credential.password_hash=source_credential.password_hash
                          WHERE source_credential.player_id=s.player_id));
                    """;
                sessions.Parameters.AddWithValue("$now", Now());
                sessions.Parameters.AddWithValue("$authoritative", source.Authoritative ? 1 : 0);
                sessions.ExecuteNonQuery();
            }

            var whitelistAdopted = TableExists(connection, transaction, schema, "qq_whitelist_state")
                && !WhitelistInitialized(connection, transaction);
            if (whitelistAdopted)
            {
                using var whitelist = connection.CreateCommand();
                whitelist.Transaction = transaction;
                whitelist.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_whitelist_state(
                        singleton_id, version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count)
                    SELECT singleton_id, version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count
                    FROM {schema}.qq_whitelist_state WHERE singleton_id=1;

                    INSERT OR IGNORE INTO shared_qq_whitelist_members(qq, version)
                    SELECT qq, version FROM {schema}.qq_whitelist_members
                    WHERE EXISTS(SELECT 1 FROM shared_qq_whitelist_state WHERE singleton_id=1);
                    """;
                whitelist.ExecuteNonQuery();
            }

            var bindingConflicts = 0;
            if (TableExists(connection, transaction, schema, "player_qq_bindings"))
            {
                bindingConflicts = ScalarInt(connection, transaction, $"""
                    SELECT COUNT(*)
                    FROM {schema}.player_qq_bindings b
                    JOIN {schema}.players p ON p.id=b.player_id
                    WHERE EXISTS(
                              SELECT 1 FROM shared_account_qq_bindings existing
                              WHERE existing.account_key=p.account_key
                                AND existing.qq IS NOT b.qq)
                       OR EXISTS(
                              SELECT 1 FROM shared_account_qq_bindings existing
                              WHERE existing.qq=b.qq
                                AND existing.account_key<>p.account_key);
                    """);
                using var bindings = connection.CreateCommand();
                bindings.Transaction = transaction;
                bindings.CommandText = $"""
                    INSERT OR IGNORE INTO shared_account_qq_bindings(
                        account_key, qq, revision, bound_at, whitelist_version, updated_at, updated_by)
                    SELECT p.account_key, b.qq, 1, b.bound_at, b.whitelist_version,
                           b.bound_at, $source
                    FROM {schema}.player_qq_bindings b
                    JOIN {schema}.players p ON p.id=b.player_id;
                    """;
                bindings.Parameters.AddWithValue("$source", $"migration:{source.Role}");
                bindings.ExecuteNonQuery();
            }
            if (whitelistAdopted
                && TableExists(connection, transaction, schema, "qq_whitelist_import_audit"))
            {
                using var importAudit = connection.CreateCommand();
                importAudit.Transaction = transaction;
                importAudit.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_whitelist_import_audit(
                        version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count)
                    SELECT version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count
                    FROM {schema}.qq_whitelist_import_audit;
                    """;
                importAudit.ExecuteNonQuery();
            }

            if (TableExists(connection, transaction, schema, "qq_bootstrap_administrators"))
            {
                using var bootstrap = connection.CreateCommand();
                bootstrap.Transaction = transaction;
                bootstrap.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_bootstrap_administrators(account_key, captured_at)
                    SELECT p.account_key, b.captured_at
                    FROM {schema}.qq_bootstrap_administrators b
                    JOIN {schema}.players p ON p.id=b.player_id;
                    """;
                bootstrap.ExecuteNonQuery();
            }

            return bindingConflicts;
        }
    }

    private static int ImportMaterializedSharedSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string schema,
        LegacyAccountSource source)
    {
        using (var accounts = connection.CreateCommand())
        {
            accounts.Transaction = transaction;
            accounts.CommandText = $"""
                INSERT OR IGNORE INTO shared_accounts(
                    account_key, account, display_name, created_at, updated_at, last_login_at)
                SELECT account_key, account, display_name, created_at, updated_at, last_login_at
                FROM {schema}.shared_accounts;

                UPDATE shared_accounts
                SET last_login_at = max(last_login_at, COALESCE((
                    SELECT source_account.last_login_at
                    FROM {schema}.shared_accounts source_account
                    WHERE source_account.account_key=shared_accounts.account_key), last_login_at));
                """;
            accounts.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_player_credentials"))
        {
            using var credentials = connection.CreateCommand();
            credentials.Transaction = transaction;
            credentials.CommandText = $"""
                INSERT OR IGNORE INTO shared_player_credentials(
                    account_key, password_hash, created_at, updated_at)
                SELECT account_key, password_hash, created_at, updated_at
                FROM {schema}.shared_player_credentials;
                """;
            credentials.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_player_auth_sessions")
            && TableExists(connection, transaction, schema, "shared_player_credentials"))
        {
            using var sessions = connection.CreateCommand();
            sessions.Transaction = transaction;
            sessions.CommandText = $"""
                INSERT OR IGNORE INTO shared_player_auth_sessions(
                    token_hash, account_key, created_at, expires_at)
                SELECT source_session.token_hash, source_session.account_key,
                       source_session.created_at, source_session.expires_at
                FROM {schema}.shared_player_auth_sessions source_session
                WHERE source_session.expires_at>$now
                  AND ($authoritative=1 OR EXISTS(
                      SELECT 1
                      FROM {schema}.shared_player_credentials source_credential
                      JOIN shared_player_credentials target_credential
                        ON target_credential.account_key=source_session.account_key
                       AND target_credential.password_hash=source_credential.password_hash
                      WHERE source_credential.account_key=source_session.account_key));
                """;
            sessions.Parameters.AddWithValue("$now", Now());
            sessions.Parameters.AddWithValue("$authoritative", source.Authoritative ? 1 : 0);
            sessions.ExecuteNonQuery();
        }

        var whitelistAdopted = TableExists(
                connection, transaction, schema, "shared_qq_whitelist_state")
            && !WhitelistInitialized(connection, transaction);
        if (whitelistAdopted)
        {
            using var whitelist = connection.CreateCommand();
            whitelist.Transaction = transaction;
            whitelist.CommandText = $"""
                INSERT OR IGNORE INTO shared_qq_whitelist_state(
                    singleton_id, version, imported_at, imported_by, member_count,
                    duplicate_count, added_count, removed_count, removed_bound_count)
                SELECT singleton_id, version, imported_at, imported_by, member_count,
                    duplicate_count, added_count, removed_count, removed_bound_count
                FROM {schema}.shared_qq_whitelist_state WHERE singleton_id=1;

                INSERT OR IGNORE INTO shared_qq_whitelist_members(qq, version)
                SELECT qq, version FROM {schema}.shared_qq_whitelist_members
                WHERE EXISTS(SELECT 1 FROM shared_qq_whitelist_state WHERE singleton_id=1);
                """;
            whitelist.ExecuteNonQuery();

            if (TableExists(connection, transaction, schema, "shared_qq_whitelist_import_audit"))
            {
                using var importAudit = connection.CreateCommand();
                importAudit.Transaction = transaction;
                importAudit.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_whitelist_import_audit(
                        version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count)
                    SELECT version, imported_at, imported_by, member_count,
                        duplicate_count, added_count, removed_count, removed_bound_count
                    FROM {schema}.shared_qq_whitelist_import_audit;
                    """;
                importAudit.ExecuteNonQuery();
            }

            if (TableExists(connection, transaction, schema, "shared_qq_whitelist_sync_runs"))
            {
                using var syncRuns = connection.CreateCommand();
                syncRuns.Transaction = transaction;
                syncRuns.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_whitelist_sync_runs(
                        operation_key, group_id, group_name, scheduled_hour, request_hash,
                        client_instance_id, version, imported_at, member_count, duplicate_count,
                        added_count, removed_count, removed_bound_count, notification_acked_at)
                    SELECT operation_key, group_id, group_name, scheduled_hour, request_hash,
                        client_instance_id, version, imported_at, member_count, duplicate_count,
                        added_count, removed_count, removed_bound_count, notification_acked_at
                    FROM {schema}.shared_qq_whitelist_sync_runs;
                    """;
                syncRuns.ExecuteNonQuery();
            }

            if (TableExists(connection, transaction, schema, "shared_qq_whitelist_update_events"))
            {
                using var updateEvents = connection.CreateCommand();
                updateEvents.Transaction = transaction;
                updateEvents.CommandText = $"""
                    INSERT OR IGNORE INTO shared_qq_whitelist_update_events(
                        event_key, outcome, source, operation_key, occurred_at, scheduled_hour,
                        version, member_count, added_count, removed_count,
                        removed_bound_count, error)
                    SELECT event_key, outcome, source, operation_key, occurred_at, scheduled_hour,
                        version, member_count, added_count, removed_count,
                        removed_bound_count, error
                    FROM {schema}.shared_qq_whitelist_update_events;
                    """;
                updateEvents.ExecuteNonQuery();
            }
        }

        var bindingConflicts = 0;
        if (TableExists(connection, transaction, schema, "shared_account_qq_bindings"))
        {
            bindingConflicts = ScalarInt(connection, transaction, $"""
                SELECT COUNT(*)
                FROM {schema}.shared_account_qq_bindings source_binding
                WHERE EXISTS(
                          SELECT 1 FROM shared_account_qq_bindings existing
                          WHERE existing.account_key=source_binding.account_key
                            AND existing.qq IS NOT source_binding.qq)
                   OR (source_binding.qq IS NOT NULL AND EXISTS(
                          SELECT 1 FROM shared_account_qq_bindings existing
                          WHERE existing.qq=source_binding.qq
                            AND existing.account_key<>source_binding.account_key));
                """);
            using var bindings = connection.CreateCommand();
            bindings.Transaction = transaction;
            bindings.CommandText = $"""
                INSERT OR IGNORE INTO shared_account_qq_bindings(
                    account_key, qq, revision, bound_at, whitelist_version, updated_at, updated_by)
                SELECT account_key, qq, revision, bound_at,
                       whitelist_version, updated_at, updated_by
                FROM {schema}.shared_account_qq_bindings;
                """;
            bindings.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_qq_bootstrap_administrators"))
        {
            using var bootstrap = connection.CreateCommand();
            bootstrap.Transaction = transaction;
            bootstrap.CommandText = $"""
                INSERT OR IGNORE INTO shared_qq_bootstrap_administrators(account_key, captured_at)
                SELECT account_key, captured_at
                FROM {schema}.shared_qq_bootstrap_administrators;
                """;
            bootstrap.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_admin_player_audit"))
        {
            using var adminAudit = connection.CreateCommand();
            adminAudit.Transaction = transaction;
            adminAudit.CommandText = $"""
                INSERT OR IGNORE INTO shared_admin_player_audit(
                    admin_account, target_account, action, request_id, detail_json, created_at)
                SELECT admin_account, target_account, action, request_id, detail_json, created_at
                FROM {schema}.shared_admin_player_audit ORDER BY id;
                """;
            adminAudit.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_qq_binding_requests"))
        {
            using var requests = connection.CreateCommand();
            requests.Transaction = transaction;
            requests.CommandText = $"""
                INSERT OR IGNORE INTO shared_qq_binding_requests(
                    admin_account, request_id, payload_hash, target_account, action,
                    resulting_revision, resulting_qq_masked, resulting_whitelisted, created_at)
                SELECT admin_account, request_id, payload_hash, target_account, action,
                    resulting_revision, resulting_qq_masked, resulting_whitelisted, created_at
                FROM {schema}.shared_qq_binding_requests;
                """;
            requests.ExecuteNonQuery();
        }

        if (TableExists(connection, transaction, schema, "shared_account_security_events"))
        {
            using var securityEvents = connection.CreateCommand();
            securityEvents.Transaction = transaction;
            securityEvents.CommandText = $"""
                INSERT INTO shared_account_security_events(
                    event_type, target_account, revision, created_at)
                SELECT event_type, target_account, revision, created_at
                FROM {schema}.shared_account_security_events ORDER BY id;
                """;
            securityEvents.ExecuteNonQuery();
        }

        return bindingConflicts;
    }

    private static void CaptureBootstrapAdministrators(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<string> accounts)
    {
        using var captured = connection.CreateCommand();
        captured.Transaction = transaction;
        captured.CommandText = "SELECT 1 FROM shared_qq_bootstrap_capture_state WHERE singleton_id=1;";
        if (captured.ExecuteScalar() is not null) return;

        var capturedAt = Now();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO shared_qq_bootstrap_administrators(account_key, captured_at)
            SELECT account_key, $capturedAt FROM shared_accounts WHERE account_key=$accountKey;
            """;
        insert.Parameters.AddWithValue("$capturedAt", capturedAt);
        var key = insert.Parameters.Add("$accountKey", SqliteType.Text);
        foreach (var account in accounts)
        {
            var normalized = (account ?? "").Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
            if (normalized.Length == 0) continue;
            key.Value = normalized;
            insert.ExecuteNonQuery();
        }

        using var mark = connection.CreateCommand();
        mark.Transaction = transaction;
        mark.CommandText = """
            INSERT INTO shared_qq_bootstrap_capture_state(singleton_id, captured_at)
            VALUES(1, $capturedAt);
            """;
        mark.Parameters.AddWithValue("$capturedAt", capturedAt);
        mark.ExecuteNonQuery();
    }

    private static SharedAccountMigrationSummary ReadSummary(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int sourceCount,
        int bindingConflicts)
    {
        var initialized = WhitelistInitialized(connection, transaction);
        var version = initialized
            ? ScalarLong(connection, transaction,
                "SELECT version FROM shared_qq_whitelist_state WHERE singleton_id=1;")
            : 0;
        return new SharedAccountMigrationSummary(
            sourceCount,
            ScalarInt(connection, transaction, "SELECT COUNT(*) FROM shared_accounts;"),
            ScalarInt(connection, transaction, "SELECT COUNT(*) FROM shared_player_credentials;"),
            ScalarInt(connection, transaction, "SELECT COUNT(*) FROM shared_player_auth_sessions;"),
            ScalarInt(connection, transaction, "SELECT COUNT(*) FROM shared_account_qq_bindings WHERE qq IS NOT NULL;"),
            bindingConflicts,
            initialized,
            version);
    }

    private static bool TableExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string schema,
        string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM {schema}.sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool WhitelistInitialized(SqliteConnection connection, SqliteTransaction transaction)
        => ScalarInt(connection, transaction,
            "SELECT COUNT(*) FROM shared_qq_whitelist_state WHERE singleton_id=1;") == 1;

    private static int ScalarInt(SqliteConnection connection, SqliteTransaction transaction, string sql)
        => checked((int)ScalarLong(connection, transaction, sql));

    private static long ScalarLong(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
