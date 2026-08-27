using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public sealed record AccountAuthenticationResult(
    bool Success,
    string Account,
    string? AuthToken,
    bool NeedsPassword,
    bool NeedsPasswordSetup,
    bool IsChallenge,
    string Message);

public sealed record PasswordChangeResult(bool Success, string? AuthToken, string Message);
public sealed record AdminPasswordResetResult(string Account, string TemporaryPassword);

/// <summary>共享账号库中的密码与短期会话令牌；密码只保存 ASP.NET Core Identity 哈希。</summary>
public sealed class AccountAuthenticationStore
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly PlayerDataStore _players;
    private readonly SharedAccountDatabase _accounts;
    private readonly PasswordHasher<string> _passwordHasher = new();
    private readonly ConcurrentDictionary<string, object> _accountLocks = new(StringComparer.Ordinal);

    public AccountAuthenticationStore(PlayerDataStore players)
        : this(players, new SharedAccountDatabase(SharedAccountDatabase.ResolveDefaultPath(players.DatabasePath)))
    {
    }

    public AccountAuthenticationStore(PlayerDataStore players, SharedAccountDatabase accounts)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    }

    public string DatabasePath => _accounts.DatabasePath;

    public void Initialize()
    {
        // Program 会先完成带迁移源的初始化；保留此幂等调用，兼容独立单元测试。
        _accounts.Initialize(
            [new LegacyAccountSource("local", _players.DatabasePath, true)],
            AdministratorPolicy.GetAuthorizedAccounts());
    }

    public AccountAuthenticationResult Authenticate(string account, string? password, string? authToken)
    {
        var normalizedAccount = NormalizeAccount(account);
        var accountKey = normalizedAccount.ToUpperInvariant();
        var accountGate = _accountLocks.GetOrAdd(accountKey, static _ => new object());

        lock (accountGate)
        {
            // 跨进程竞争由最终的 SQLite 立即事务线性化。密码哈希和校验故意放在写事务外，
            // 避免一个环境的慢哈希阻塞另一环境的所有账号登录。
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var connection = _accounts.OpenConnection();
                var identity = FindAccount(connection, transaction: null, accountKey);
                if (identity is null)
                {
                    if (string.IsNullOrEmpty(password))
                        return Challenge(normalizedAccount, setup: true, "这是新账号，请设置登录密码。");
                    var validationError = ValidatePassword(password);
                    if (validationError is not null)
                        return Failure(normalizedAccount, setup: true, validationError);
                    var passwordHash = _passwordHasher.HashPassword(normalizedAccount, password);

                    using var transaction = connection.BeginTransaction(deferred: false);
                    if (FindAccount(connection, transaction, accountKey) is not null) continue;
                    var now = Now();
                    using (var insertAccount = connection.CreateCommand())
                    {
                        insertAccount.Transaction = transaction;
                        insertAccount.CommandText = """
                            INSERT INTO shared_accounts(
                                account_key, account, display_name, created_at, updated_at, last_login_at)
                            VALUES($accountKey, $account, $displayName, $now, $now, $now);
                            """;
                        insertAccount.Parameters.AddWithValue("$accountKey", accountKey);
                        insertAccount.Parameters.AddWithValue("$account", normalizedAccount);
                        insertAccount.Parameters.AddWithValue("$displayName", normalizedAccount);
                        insertAccount.Parameters.AddWithValue("$now", now);
                        insertAccount.ExecuteNonQuery();
                    }
                    InsertCredential(connection, transaction, accountKey, passwordHash);
                    var token = IssueSession(connection, transaction, accountKey);
                    transaction.Commit();
                    EnsureLocalPlayer(normalizedAccount, normalizedAccount);
                    return Success(normalizedAccount, token, "账号已创建并设置密码。");
                }

                var canonicalAccount = identity.Value.Account;
                if (identity.Value.PasswordHash is null)
                {
                    if (string.IsNullOrEmpty(password))
                        return Challenge(canonicalAccount, setup: true, "该账号需要先设置登录密码。");
                    var validationError = ValidatePassword(password);
                    if (validationError is not null)
                        return Failure(canonicalAccount, setup: true, validationError);
                    var passwordHash = _passwordHasher.HashPassword(canonicalAccount, password);

                    using var transaction = connection.BeginTransaction(deferred: false);
                    var latest = FindAccount(connection, transaction, accountKey);
                    if (latest is null || latest.Value.PasswordHash is not null) continue;
                    InsertCredential(connection, transaction, accountKey, passwordHash);
                    TouchLogin(connection, transaction, accountKey);
                    var token = IssueSession(connection, transaction, accountKey);
                    transaction.Commit();
                    EnsureLocalPlayer(canonicalAccount, latest.Value.DisplayName);
                    return Success(canonicalAccount, token, "密码设置成功，已登录。");
                }

                if (!string.IsNullOrWhiteSpace(authToken)
                    && ValidateSession(connection, transaction: null, accountKey, authToken))
                {
                    using var transaction = connection.BeginTransaction(deferred: false);
                    var latest = FindAccount(connection, transaction, accountKey);
                    if (latest is not null && ValidateSession(connection, transaction, accountKey, authToken))
                    {
                        TouchLogin(connection, transaction, accountKey);
                        transaction.Commit();
                        EnsureLocalPlayer(latest.Value.Account, latest.Value.DisplayName);
                        return Success(latest.Value.Account, authToken, "登录成功");
                    }
                }

                if (string.IsNullOrEmpty(password))
                    return Challenge(canonicalAccount, setup: false, "请输入密码登录。");

                var verification = _passwordHasher.VerifyHashedPassword(
                    canonicalAccount, identity.Value.PasswordHash, password);
                if (verification == PasswordVerificationResult.Failed)
                    return Failure(canonicalAccount, setup: false, "账号或密码错误。");
                var rehashed = verification == PasswordVerificationResult.SuccessRehashNeeded
                    ? _passwordHasher.HashPassword(canonicalAccount, password)
                    : null;

                using (var transaction = connection.BeginTransaction(deferred: false))
                {
                    var latest = FindAccount(connection, transaction, accountKey);
                    if (latest?.PasswordHash is null
                        || !string.Equals(latest.Value.PasswordHash, identity.Value.PasswordHash, StringComparison.Ordinal))
                        continue;
                    if (rehashed is not null)
                        UpdateCredential(connection, transaction, accountKey, rehashed);
                    TouchLogin(connection, transaction, accountKey);
                    var issued = IssueSession(connection, transaction, accountKey);
                    transaction.Commit();
                    EnsureLocalPlayer(latest.Value.Account, latest.Value.DisplayName);
                    return Success(latest.Value.Account, issued, "登录成功");
                }
            }

            return Failure(normalizedAccount, setup: false, "账号认证资料正在变更，请稍后重试。");
        }
    }

    public PasswordChangeResult ChangePassword(string account, string currentPassword, string newPassword)
    {
        var normalizedAccount = NormalizeAccount(account);
        var accountKey = normalizedAccount.ToUpperInvariant();
        var accountGate = _accountLocks.GetOrAdd(accountKey, static _ => new object());

        lock (accountGate)
        {
            var validationError = ValidatePassword(newPassword);
            if (validationError is not null) return new(false, null, validationError);
            if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
                return new(false, null, "新密码不能与当前密码相同。");

            using var connection = _accounts.OpenConnection();
            var identity = FindAccount(connection, transaction: null, accountKey);
            if (identity is null || identity.Value.PasswordHash is null)
                return new(false, null, "账号认证资料不存在，请重新登录。");

            var verification = _passwordHasher.VerifyHashedPassword(
                identity.Value.Account, identity.Value.PasswordHash, currentPassword ?? "");
            if (verification == PasswordVerificationResult.Failed)
                return new(false, null, "当前密码不正确。");
            var newPasswordHash = _passwordHasher.HashPassword(identity.Value.Account, newPassword);

            using var transaction = connection.BeginTransaction(deferred: false);
            var latest = FindAccount(connection, transaction, accountKey);
            if (latest?.PasswordHash is null
                || !string.Equals(latest.Value.PasswordHash, identity.Value.PasswordHash, StringComparison.Ordinal))
                return new(false, null, "账号密码已在其他会话中变更，请重新登录后再试。");
            UpdateCredential(
                connection,
                transaction,
                accountKey,
                newPasswordHash);
            DeleteSessions(connection, transaction, accountKey);
            var token = IssueSession(connection, transaction, accountKey);
            transaction.Commit();
            return new(true, token, "密码修改成功，测试服与正式服的其他已登录会话均已失效。");
        }
    }

    public AdminPasswordResetResult AdminResetPassword(string adminAccount, string targetAccount)
    {
        var normalizedAdmin = NormalizeAccount(adminAccount);
        var normalizedTarget = NormalizeAccount(targetAccount);
        var accountKey = normalizedTarget.ToUpperInvariant();
        var accountGate = _accountLocks.GetOrAdd(accountKey, static _ => new object());

        lock (accountGate)
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var identity = FindAccount(connection, transaction, accountKey)
                ?? throw new PlayerDataValidationException("玩家账号不存在。");
            if (string.Equals(normalizedAdmin, identity.Account, StringComparison.OrdinalIgnoreCase))
                throw new PlayerDataValidationException("不能在当前管理员会话中重置自己的密码，请由另一位管理员操作。");

            var temporaryPassword = CreateTemporaryPassword();
            using (var credential = connection.CreateCommand())
            {
                credential.Transaction = transaction;
                credential.CommandText = """
                    INSERT INTO shared_player_credentials(account_key, password_hash, created_at, updated_at)
                    VALUES($accountKey, $passwordHash, $now, $now)
                    ON CONFLICT(account_key) DO UPDATE SET
                        password_hash=excluded.password_hash,
                        updated_at=excluded.updated_at;
                    """;
                credential.Parameters.AddWithValue("$accountKey", accountKey);
                credential.Parameters.AddWithValue(
                    "$passwordHash",
                    _passwordHasher.HashPassword(identity.Account, temporaryPassword));
                credential.Parameters.AddWithValue("$now", Now());
                credential.ExecuteNonQuery();
            }
            DeleteSessions(connection, transaction, accountKey);
            InsertAdminAudit(
                connection,
                transaction,
                normalizedAdmin,
                identity.Account,
                "reset_password",
                requestId: null,
                "{}");
            InsertSecurityEvent(connection, transaction, "credentials_reset", identity.Account, revision: null);
            transaction.Commit();
            return new AdminPasswordResetResult(identity.Account, temporaryPassword);
        }
    }

    /// <summary>
    /// 更新跨环境管理员检索目录中的昵称。各环境 players.db 内的玩法显示昵称仍彼此隔离，
    /// 此字段不会反向覆盖其他环境已经物化的玩家资料。
    /// </summary>
    public void UpdateDirectorySearchName(string account, string displayName)
    {
        var normalizedAccount = NormalizeAccount(account);
        var normalizedDisplayName = (displayName ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalizedDisplayName.Length is < 1 or > PlayerDataStore.MaxDisplayNameLength
            || normalizedDisplayName.Any(char.IsControl))
            throw new PlayerDataValidationException("昵称格式无效。");
        using var connection = _accounts.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE shared_accounts
            SET display_name=$displayName, updated_at=$updatedAt
            WHERE account_key=$accountKey;
            """;
        update.Parameters.AddWithValue("$displayName", normalizedDisplayName);
        update.Parameters.AddWithValue("$updatedAt", Now());
        update.Parameters.AddWithValue("$accountKey", normalizedAccount.ToUpperInvariant());
        if (update.ExecuteNonQuery() == 0)
            throw new PlayerDataValidationException("玩家账号不存在。");
        transaction.Commit();
    }

    private void EnsureLocalPlayer(string account, string displayName)
    {
        // 共享账号是身份权威源；目录昵称只用于本环境首次物化。已经存在的玩法显示昵称不跨环境覆盖。
        _players.LoginSharedAccount(account, displayName);
    }

    private static SharedCredential? FindAccount(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string accountKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.account, a.display_name, c.password_hash
            FROM shared_accounts a
            LEFT JOIN shared_player_credentials c ON c.account_key=a.account_key
            WHERE a.account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$accountKey", accountKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new SharedCredential(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static void InsertCredential(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey,
        string passwordHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO shared_player_credentials(account_key, password_hash, created_at, updated_at)
            VALUES($accountKey, $passwordHash, $now, $now);
            """;
        command.Parameters.AddWithValue("$accountKey", accountKey);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
    }

    private static void UpdateCredential(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey,
        string passwordHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE shared_player_credentials
            SET password_hash=$passwordHash, updated_at=$now
            WHERE account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$now", Now());
        command.Parameters.AddWithValue("$accountKey", accountKey);
        command.ExecuteNonQuery();
    }

    private static bool ValidateSession(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string accountKey,
        string token)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM shared_player_auth_sessions
            WHERE token_hash=$tokenHash AND account_key=$accountKey AND expires_at>$now;
            """;
        command.Parameters.AddWithValue("$tokenHash", HashToken(token));
        command.Parameters.AddWithValue("$accountKey", accountKey);
        command.Parameters.AddWithValue("$now", Now());
        return command.ExecuteScalar() is not null;
    }

    private static string IssueSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey)
    {
        var token = CreateToken();
        var now = Now();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM shared_player_auth_sessions
            WHERE account_key=$accountKey AND expires_at<=$now;
            INSERT INTO shared_player_auth_sessions(token_hash, account_key, created_at, expires_at)
            VALUES($tokenHash, $accountKey, $now, $expiresAt);
            DELETE FROM shared_player_auth_sessions
            WHERE account_key=$accountKey
              AND token_hash NOT IN (
                  SELECT token_hash FROM shared_player_auth_sessions
                  WHERE account_key=$accountKey ORDER BY created_at DESC LIMIT 10
              );
            """;
        command.Parameters.AddWithValue("$accountKey", accountKey);
        command.Parameters.AddWithValue("$tokenHash", HashToken(token));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$expiresAt", now + (long)SessionLifetime.TotalMilliseconds);
        command.ExecuteNonQuery();
        return token;
    }

    private static void DeleteSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey)
    {
        using var invalidate = connection.CreateCommand();
        invalidate.Transaction = transaction;
        invalidate.CommandText = "DELETE FROM shared_player_auth_sessions WHERE account_key=$accountKey;";
        invalidate.Parameters.AddWithValue("$accountKey", accountKey);
        invalidate.ExecuteNonQuery();
    }

    private static void TouchLogin(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountKey)
    {
        using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText = "UPDATE shared_accounts SET last_login_at=$now WHERE account_key=$accountKey;";
        touch.Parameters.AddWithValue("$now", Now());
        touch.Parameters.AddWithValue("$accountKey", accountKey);
        touch.ExecuteNonQuery();
    }

    internal static void InsertAdminAudit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string adminAccount,
        string targetAccount,
        string action,
        string? requestId,
        string detailJson)
    {
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = """
            INSERT INTO shared_admin_player_audit(
                admin_account, target_account, action, request_id, detail_json, created_at)
            VALUES($admin, $target, $action, $requestId, $detail, $createdAt);
            """;
        audit.Parameters.AddWithValue("$admin", adminAccount);
        audit.Parameters.AddWithValue("$target", targetAccount);
        audit.Parameters.AddWithValue("$action", action);
        audit.Parameters.AddWithValue("$requestId", (object?)requestId ?? DBNull.Value);
        audit.Parameters.AddWithValue("$detail", detailJson);
        audit.Parameters.AddWithValue("$createdAt", Now());
        audit.ExecuteNonQuery();
    }

    internal static void InsertSecurityEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventType,
        string? targetAccount,
        long? revision)
    {
        using var securityEvent = connection.CreateCommand();
        securityEvent.Transaction = transaction;
        securityEvent.CommandText = """
            INSERT INTO shared_account_security_events(event_type, target_account, revision, created_at)
            VALUES($eventType, $targetAccount, $revision, $createdAt);
            """;
        securityEvent.Parameters.AddWithValue("$eventType", eventType);
        securityEvent.Parameters.AddWithValue("$targetAccount", (object?)targetAccount ?? DBNull.Value);
        securityEvent.Parameters.AddWithValue("$revision", (object?)revision ?? DBNull.Value);
        securityEvent.Parameters.AddWithValue("$createdAt", Now());
        securityEvent.ExecuteNonQuery();
    }

    private static string NormalizeAccount(string account)
    {
        var normalized = (account ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > PlayerDataStore.MaxAccountLength)
            throw new PlayerDataValidationException($"账号长度需为 1–{PlayerDataStore.MaxAccountLength} 个字符。");
        if (normalized.Any(char.IsControl))
            throw new PlayerDataValidationException("账号不能包含控制字符。");
        return normalized;
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
            return $"密码长度需为 {MinPasswordLength}–{MaxPasswordLength} 个字符。";
        return null;
    }

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 18)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])
            .ToArray());
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static AccountAuthenticationResult Success(string account, string token, string message)
        => new(true, account, token, false, false, false, message);

    private static AccountAuthenticationResult Challenge(string account, bool setup, string message)
        => new(false, account, null, true, setup, true, message);

    private static AccountAuthenticationResult Failure(string account, bool setup, string message)
        => new(false, account, null, true, setup, false, message);

    private readonly record struct SharedCredential(
        string Account,
        string DisplayName,
        string? PasswordHash);
}
