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

/// <summary>账号密码与短期会话令牌存储。密码仅以 ASP.NET Core Identity 哈希格式保存。</summary>
public sealed class AccountAuthenticationStore
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly PlayerDataStore _players;
    private readonly string _connectionString;
    private readonly PasswordHasher<string> _passwordHasher = new();
    private readonly ConcurrentDictionary<string, object> _accountLocks = new(StringComparer.Ordinal);

    public AccountAuthenticationStore(PlayerDataStore players)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = players.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS player_credentials (
                player_id     INTEGER PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
                password_hash TEXT NOT NULL,
                created_at    INTEGER NOT NULL,
                updated_at    INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS player_auth_sessions (
                token_hash TEXT PRIMARY KEY,
                player_id  INTEGER NOT NULL REFERENCES players(id) ON DELETE CASCADE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_player_auth_sessions_player
                ON player_auth_sessions(player_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_player_auth_sessions_expiry
                ON player_auth_sessions(expires_at);

            DELETE FROM player_auth_sessions WHERE expires_at <= $now;
            """;
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
    }

    public AccountAuthenticationResult Authenticate(string account, string? password, string? authToken)
    {
        var normalizedAccount = NormalizeAccount(account);
        var accountKey = normalizedAccount.ToUpperInvariant();
        var accountGate = _accountLocks.GetOrAdd(accountKey, static _ => new object());

        lock (accountGate)
        {
            using var connection = OpenConnection();
            var player = FindPlayer(connection, accountKey);
            if (player is null)
            {
                if (string.IsNullOrEmpty(password))
                    return Challenge(normalizedAccount, setup: true, "这是新账号，请设置登录密码。");

                var validationError = ValidatePassword(password);
                if (validationError is not null)
                    return Failure(normalizedAccount, setup: true, validationError);

                var snapshot = _players.Login(normalizedAccount);
                player = FindPlayer(connection, accountKey)
                    ?? throw new InvalidOperationException("新账号创建后无法读取认证资料。");
                var passwordHash = _passwordHasher.HashPassword(snapshot.Account, password);
                InsertCredential(connection, player.Value.PlayerId, passwordHash);
                var token = IssueSession(connection, player.Value.PlayerId);
                return Success(snapshot.Account, token, "账号已创建并设置密码。");
            }

            var canonicalAccount = player.Value.Account;
            if (player.Value.PasswordHash is null)
            {
                if (string.IsNullOrEmpty(password))
                    return Challenge(canonicalAccount, setup: true, "该账号需要先设置登录密码。");

                var validationError = ValidatePassword(password);
                if (validationError is not null)
                    return Failure(canonicalAccount, setup: true, validationError);

                var passwordHash = _passwordHasher.HashPassword(canonicalAccount, password);
                InsertCredential(connection, player.Value.PlayerId, passwordHash);
                var token = IssueSession(connection, player.Value.PlayerId);
                return Success(canonicalAccount, token, "密码设置成功，已登录。");
            }

            if (!string.IsNullOrWhiteSpace(authToken) && ValidateSession(connection, player.Value.PlayerId, authToken))
                return Success(canonicalAccount, authToken, "登录成功");

            if (string.IsNullOrEmpty(password))
                return Challenge(canonicalAccount, setup: false, "请输入密码登录。");

            var verification = _passwordHasher.VerifyHashedPassword(
                canonicalAccount, player.Value.PasswordHash, password);
            if (verification == PasswordVerificationResult.Failed)
                return Failure(canonicalAccount, setup: false, "账号或密码错误。");

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                UpdateCredential(connection, player.Value.PlayerId,
                    _passwordHasher.HashPassword(canonicalAccount, password));

            return Success(canonicalAccount, IssueSession(connection, player.Value.PlayerId), "登录成功");
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

            using var connection = OpenConnection();
            var player = FindPlayer(connection, accountKey);
            if (player is null || player.Value.PasswordHash is null)
                return new(false, null, "账号认证资料不存在，请重新登录。");

            var verification = _passwordHasher.VerifyHashedPassword(
                player.Value.Account, player.Value.PasswordHash, currentPassword ?? "");
            if (verification == PasswordVerificationResult.Failed)
                return new(false, null, "当前密码不正确。");

            using var transaction = connection.BeginTransaction();
            UpdateCredential(
                connection,
                player.Value.PlayerId,
                _passwordHasher.HashPassword(player.Value.Account, newPassword),
                transaction);
            using (var invalidate = connection.CreateCommand())
            {
                invalidate.Transaction = transaction;
                invalidate.CommandText = "DELETE FROM player_auth_sessions WHERE player_id=$playerId;";
                invalidate.Parameters.AddWithValue("$playerId", player.Value.PlayerId);
                invalidate.ExecuteNonQuery();
            }
            var token = IssueSession(connection, player.Value.PlayerId, transaction);
            transaction.Commit();
            return new(true, token, "密码修改成功，其他已登录会话已失效。");
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
            using var connection = OpenConnection();
            var player = FindPlayer(connection, accountKey)
                ?? throw new PlayerDataValidationException("玩家账号不存在。");
            if (string.Equals(normalizedAdmin, player.Account, StringComparison.OrdinalIgnoreCase))
                throw new PlayerDataValidationException("不能在当前管理员会话中重置自己的密码，请由另一位管理员操作。");
            var temporaryPassword = CreateTemporaryPassword();
            using var transaction = connection.BeginTransaction();
            using (var credential = connection.CreateCommand())
            {
                credential.Transaction = transaction;
                credential.CommandText = """
                    INSERT INTO player_credentials(player_id, password_hash, created_at, updated_at)
                    VALUES($playerId, $passwordHash, $now, $now)
                    ON CONFLICT(player_id) DO UPDATE SET
                        password_hash=excluded.password_hash,
                        updated_at=excluded.updated_at;
                    DELETE FROM player_auth_sessions WHERE player_id=$playerId;
                    """;
                credential.Parameters.AddWithValue("$playerId", player.PlayerId);
                credential.Parameters.AddWithValue(
                    "$passwordHash",
                    _passwordHasher.HashPassword(player.Account, temporaryPassword));
                credential.Parameters.AddWithValue("$now", Now());
                credential.ExecuteNonQuery();
            }
            PlayerDataStore.InsertAdminAudit(
                connection,
                transaction,
                normalizedAdmin,
                player.Account,
                "reset_password",
                "{}");
            transaction.Commit();
            return new AdminPasswordResetResult(player.Account, temporaryPassword);
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

    private static PlayerCredential? FindPlayer(SqliteConnection connection, string accountKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.account, c.password_hash
            FROM players p
            LEFT JOIN player_credentials c ON c.player_id=p.id
            WHERE p.account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$accountKey", accountKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new PlayerCredential(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static void InsertCredential(SqliteConnection connection, long playerId, string passwordHash)
    {
        var now = Now();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO player_credentials(player_id, password_hash, created_at, updated_at)
            VALUES($playerId, $passwordHash, $now, $now);
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static void UpdateCredential(
        SqliteConnection connection,
        long playerId,
        string passwordHash,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE player_credentials
            SET password_hash=$passwordHash, updated_at=$now
            WHERE player_id=$playerId;
            """;
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$now", Now());
        command.Parameters.AddWithValue("$playerId", playerId);
        command.ExecuteNonQuery();
    }

    private static bool ValidateSession(SqliteConnection connection, long playerId, string token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM player_auth_sessions
            WHERE token_hash=$tokenHash AND player_id=$playerId AND expires_at>$now;
            """;
        command.Parameters.AddWithValue("$tokenHash", HashToken(token));
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$now", Now());
        return command.ExecuteScalar() is not null;
    }

    private static string IssueSession(
        SqliteConnection connection,
        long playerId,
        SqliteTransaction? transaction = null)
    {
        var token = CreateToken();
        var now = Now();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM player_auth_sessions WHERE player_id=$playerId AND expires_at<=$now;
            INSERT INTO player_auth_sessions(token_hash, player_id, created_at, expires_at)
            VALUES($tokenHash, $playerId, $now, $expiresAt);
            DELETE FROM player_auth_sessions
            WHERE player_id=$playerId
              AND token_hash NOT IN (
                  SELECT token_hash FROM player_auth_sessions
                  WHERE player_id=$playerId ORDER BY created_at DESC LIMIT 10
              );
            """;
        command.Parameters.AddWithValue("$playerId", playerId);
        command.Parameters.AddWithValue("$tokenHash", HashToken(token));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$expiresAt", now + (long)SessionLifetime.TotalMilliseconds);
        command.ExecuteNonQuery();
        return token;
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

    private readonly record struct PlayerCredential(long PlayerId, string Account, string? PasswordHash);
}
