using System.Collections.Concurrent;
using System.Security.Cryptography;
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

public sealed record QqWhitelistScheduledSyncRequest(
    string OperationKey,
    long ScheduledHour,
    string GroupId,
    string GroupName,
    int ReportedMemberCount,
    string ClientInstanceId,
    string MembersJson);

public sealed record QqWhitelistScheduledSyncResult(
    string OperationKey,
    long ScheduledHour,
    string GroupId,
    string GroupName,
    string ClientInstanceId,
    QqWhitelistImportResult Import,
    bool Replayed,
    bool NotificationOwner,
    long? NotificationAcknowledgedAt);

public sealed record QqAccountBindingStatus(
    bool Bound,
    string? MaskedQq,
    bool CurrentlyWhitelisted,
    long? BoundAt,
    long Revision = 0);

public sealed record AdminQqAccountSummary(
    string Account,
    string DisplayName,
    long CreatedAt,
    long LastLoginAt,
    bool HasPassword,
    string? Qq,
    string? QqMasked,
    bool QqCurrentlyWhitelisted,
    long? QqBoundAt,
    long BindingRevision,
    string MatchKind);

public sealed record AdminQqBindingMutationResult(
    string Account,
    string DisplayName,
    string? Qq,
    string? QqMasked,
    bool CurrentlyWhitelisted,
    long? BoundAt,
    long Revision,
    bool Replayed);

public sealed record AccountSecurityEvent(
    long Id,
    string EventType,
    string? TargetAccount,
    long? Revision,
    long CreatedAt);

public sealed class QqAccessValidationException : Exception
{
    public QqAccessValidationException(string message) : base(message) { }
    public QqAccessValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class QqAccessDeniedException(string message) : Exception(message);

/// <summary>
/// QQ 群白名单与账号唯一绑定的共享权威存储。跨进程竞争依赖 SQLite 立即事务线性化；
/// 进程内读写锁只负责减少同库竞争，并不是一致性的唯一来源。
/// </summary>
public sealed partial class QqAccessStore
{
    public const int MinQqLength = 5;
    public const int MaxQqLength = 12;
    public const int MaxImportBytes = 256 * 1024;
    public const int MaxImportMembers = 10_000;
    public const int MaxAdminSearchResults = 50;
    public const int MaxSyncOperationKeyLength = 128;
    public const int MaxSyncGroupNameLength = 100;
    public const string NewGameDeniedMessage = "QQ 群白名单资格无效，无法进入新对局。请确认仍在群名单内并重新登录。";

    private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> DatabaseGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SharedAccountDatabase _accounts;
    private readonly ReaderWriterLockSlim _gate;
    private readonly string[] _bootstrapAdministratorAccounts;

    public QqAccessStore(PlayerDataStore players, IEnumerable<string>? bootstrapAdministratorAccounts = null)
        : this(
            new SharedAccountDatabase(SharedAccountDatabase.ResolveDefaultPath(players.DatabasePath)),
            bootstrapAdministratorAccounts)
    {
    }

    public QqAccessStore(
        SharedAccountDatabase accounts,
        IEnumerable<string>? bootstrapAdministratorAccounts = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _gate = DatabaseGates.GetOrAdd(accounts.DatabasePath, static _ => new ReaderWriterLockSlim());
        _bootstrapAdministratorAccounts = (bootstrapAdministratorAccounts ?? [])
            .Select(NormalizeAccount)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string DatabasePath => _accounts.DatabasePath;

    public void Initialize()
        => _accounts.Initialize(bootstrapAdministratorAccounts: _bootstrapAdministratorAccounts);

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
            using var connection = _accounts.OpenConnection();
            return ReadStatus(connection, transaction: null);
        }
        finally { _gate.ExitReadLock(); }
    }

    public bool IsBootstrapAdministrator(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1 FROM shared_qq_bootstrap_administrators
                WHERE account_key=$accountKey;
                """;
            command.Parameters.AddWithValue("$accountKey", normalizedAccount.ToUpperInvariant());
            return command.ExecuteScalar() is not null;
        }
        finally { _gate.ExitReadLock(); }
    }

    public QqWhitelistImportResult Import(string adminAccount, string json, bool initializationOnly = false)
    {
        var normalizedAdmin = NormalizeAccount(adminAccount);
        var parsed = ParseImport(json);

        _gate.EnterWriteLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var result = ImportParsed(
                connection, transaction, normalizedAdmin, parsed, initializationOnly);
            transaction.Commit();
            return result;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessValidationException("共享账号数据库正忙，请稍后重试。", ex);
        }
        finally { _gate.ExitWriteLock(); }
    }

    /// <summary>
    /// 接收 QQ 机器人整点快照。幂等记录与白名单替换在同一个立即事务内提交，
    /// 因而跨进程重复请求、进程重启和部分写入都不会产生第二个版本。
    /// </summary>
    public QqWhitelistScheduledSyncResult SynchronizeScheduledGroup(
        QqWhitelistScheduledSyncRequest request,
        string expectedGroupId,
        string expectedGroupName,
        int minimumMemberCount,
        int maximumShrinkPercent,
        int maximumDelaySeconds,
        long nowUnixSeconds)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeScheduledSyncRequest(request, expectedGroupId, expectedGroupName);
        var parsed = ParseImport(normalized.MembersJson);
        if (parsed.DuplicateCount != 0)
            throw new QqAccessValidationException("QQ 群成员快照包含重复账号，拒绝自动同步。");
        if (normalized.ReportedMemberCount != parsed.TotalCount)
            throw new QqAccessValidationException("QQ 群信息接口与成员列表的人数不一致，拒绝自动同步。");
        if (parsed.QqNumbers.Count < Math.Clamp(minimumMemberCount, 1, MaxImportMembers))
            throw new QqAccessValidationException("QQ 群成员数量低于安全下限，拒绝自动同步。");
        maximumShrinkPercent = Math.Clamp(maximumShrinkPercent, 0, 99);
        maximumDelaySeconds = Math.Clamp(maximumDelaySeconds, 1, 3599);
        var requestHash = HashScheduledSyncRequest(normalized, parsed.QqNumbers);

        _gate.EnterWriteLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var replay = ReadScheduledSyncRun(connection, transaction, normalized.OperationKey);
            if (replay is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(replay.RequestHash),
                        Convert.FromHexString(requestHash)))
                    throw new QqAccessValidationException("同一整点幂等键对应的成员快照不一致，拒绝覆盖已提交版本。");
                transaction.Commit();
                return ToScheduledSyncResult(replay, normalized.ClientInstanceId, replayed: true);
            }

            var competing = ReadScheduledSyncRunByHour(
                connection, transaction, normalized.GroupId, normalized.ScheduledHour);
            if (competing is not null)
                throw new QqAccessValidationException("该 QQ 群本整点已经完成过白名单同步。");

            ValidateFreshScheduledHour(normalized.ScheduledHour, nowUnixSeconds, maximumDelaySeconds);
            var previousStatus = ReadStatus(connection, transaction);
            if (previousStatus.Initialized
                && (long)parsed.QqNumbers.Count * 100
                    < (long)previousStatus.MemberCount * (100 - maximumShrinkPercent))
                throw new QqAccessValidationException("QQ 群成员数量相较当前白名单显著缩水，拒绝自动同步。");

            var importedBy = $"qq-sync:{normalized.GroupId}";
            var imported = ImportParsed(
                connection, transaction, importedBy, parsed, initializationOnly: false);
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO shared_qq_whitelist_sync_runs(
                        operation_key, group_id, group_name, scheduled_hour, request_hash,
                        client_instance_id, version, imported_at, member_count, duplicate_count,
                        added_count, removed_count, removed_bound_count, notification_acked_at)
                    VALUES($operationKey, $groupId, $groupName, $scheduledHour, $requestHash,
                        $clientInstanceId, $version, $importedAt, $memberCount, $duplicateCount,
                        $addedCount, $removedCount, $removedBoundCount, NULL);
                    """;
                insert.Parameters.AddWithValue("$operationKey", normalized.OperationKey);
                insert.Parameters.AddWithValue("$groupId", normalized.GroupId);
                insert.Parameters.AddWithValue("$groupName", normalized.GroupName);
                insert.Parameters.AddWithValue("$scheduledHour", normalized.ScheduledHour);
                insert.Parameters.AddWithValue("$requestHash", requestHash);
                insert.Parameters.AddWithValue("$clientInstanceId", normalized.ClientInstanceId);
                AddImportParameters(insert, imported.Version, imported.ImportedAt, importedBy,
                    imported.MemberCount, imported.DuplicateCount, imported.AddedCount,
                    imported.RemovedCount, imported.RemovedBoundCount);
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
            var stored = new StoredScheduledSyncRun(
                normalized.OperationKey, normalized.ScheduledHour, normalized.GroupId,
                normalized.GroupName, requestHash, normalized.ClientInstanceId, imported, null);
            return ToScheduledSyncResult(stored, normalized.ClientInstanceId, replayed: false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessValidationException("共享账号数据库正忙，请稍后重试。", ex);
        }
        finally { _gate.ExitWriteLock(); }
    }

    public QqWhitelistScheduledSyncResult? GetScheduledGroupSync(
        string operationKey,
        string clientInstanceId)
    {
        var normalizedOperationKey = NormalizeSyncOperationKey(operationKey);
        var normalizedClientId = NormalizeClientInstanceId(clientInstanceId);
        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            var stored = ReadScheduledSyncRun(connection, transaction: null, normalizedOperationKey);
            return stored is null
                ? null
                : ToScheduledSyncResult(stored, normalizedClientId, replayed: true);
        }
        finally { _gate.ExitReadLock(); }
    }

    public QqWhitelistScheduledSyncResult AcknowledgeScheduledGroupNotification(
        string operationKey,
        string clientInstanceId,
        long version)
    {
        var normalizedOperationKey = NormalizeSyncOperationKey(operationKey);
        var normalizedClientId = NormalizeClientInstanceId(clientInstanceId);
        _gate.EnterWriteLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var stored = ReadScheduledSyncRun(connection, transaction, normalizedOperationKey)
                ?? throw new QqAccessValidationException("白名单同步记录不存在。");
            if (!string.Equals(stored.ClientInstanceId, normalizedClientId, StringComparison.Ordinal))
                throw new QqAccessValidationException("当前机器人实例不是该整点通知的所有者。");
            if (stored.Import.Version != version)
                throw new QqAccessValidationException("通知确认的白名单版本与已提交版本不一致。");
            var acknowledgedAt = stored.NotificationAcknowledgedAt ?? Now();
            if (stored.NotificationAcknowledgedAt is null)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE shared_qq_whitelist_sync_runs
                    SET notification_acked_at=$acknowledgedAt
                    WHERE operation_key=$operationKey AND notification_acked_at IS NULL;
                    """;
                update.Parameters.AddWithValue("$acknowledgedAt", acknowledgedAt);
                update.Parameters.AddWithValue("$operationKey", normalizedOperationKey);
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            return ToScheduledSyncResult(
                stored with { NotificationAcknowledgedAt = acknowledgedAt },
                normalizedClientId,
                replayed: stored.NotificationAcknowledgedAt is not null);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessValidationException("共享账号数据库正忙，请稍后重试。", ex);
        }
        finally { _gate.ExitWriteLock(); }
    }

    /// <summary>凭证认证通过后调用；只有 Allowed 才能建立普通业务会话。</summary>
    public QqLoginAccessResult EvaluateLogin(string account, string? submittedQq)
    {
        var normalizedAccount = NormalizeAccount(account);
        var normalizedQq = submittedQq is null ? null : NormalizeQq(submittedQq);

        _gate.EnterUpgradeableReadLock();
        try
        {
            using (var connection = _accounts.OpenConnection())
            {
                var status = ReadStatus(connection, transaction: null);
                if (!status.Initialized)
                    return new QqLoginAccessResult(QqLoginAccessKind.WhitelistUninitialized,
                        "QQ 群白名单尚未初始化，请联系管理员。");
                var binding = ReadBinding(connection, transaction: null, normalizedAccount);
                if (binding is { Qq: not null })
                {
                    if (normalizedQq is not null && !string.Equals(binding.Qq, normalizedQq, StringComparison.Ordinal))
                        return new QqLoginAccessResult(QqLoginAccessKind.QqAlreadyBound,
                            "该账号已经绑定 QQ，玩家不能自行更换绑定。", status.Version, MaskQq(binding.Qq));
                    return IsWhitelisted(connection, transaction: null, binding.Qq)
                        ? new QqLoginAccessResult(QqLoginAccessKind.Allowed,
                            "QQ 群白名单验证通过。", status.Version, MaskQq(binding.Qq))
                        : new QqLoginAccessResult(QqLoginAccessKind.NotWhitelisted,
                            "绑定 QQ 当前不在群白名单内，无法登录。", status.Version, MaskQq(binding.Qq));
                }
                if (normalizedQq is null)
                    return new QqLoginAccessResult(QqLoginAccessKind.NeedsBinding,
                        "首次登录需要绑定当前群白名单内的 QQ。绑定后玩家不能自行更换。", status.Version);
            }

            _gate.EnterWriteLock();
            try
            {
                using var connection = _accounts.OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var status = ReadStatus(connection, transaction);
                if (!status.Initialized)
                    return new QqLoginAccessResult(QqLoginAccessKind.WhitelistUninitialized,
                        "QQ 群白名单尚未初始化，请联系管理员。");

                var existing = ReadBinding(connection, transaction, normalizedAccount);
                if (existing is { Qq: not null })
                {
                    if (!string.Equals(existing.Qq, normalizedQq, StringComparison.Ordinal))
                        return new QqLoginAccessResult(QqLoginAccessKind.QqAlreadyBound,
                            "该账号已经绑定 QQ，玩家不能自行更换绑定。", status.Version, MaskQq(existing.Qq));
                    if (!IsWhitelisted(connection, transaction, existing.Qq))
                        return new QqLoginAccessResult(QqLoginAccessKind.NotWhitelisted,
                            "绑定 QQ 当前不在群白名单内，无法登录。", status.Version, MaskQq(existing.Qq));
                    transaction.Commit();
                    return new QqLoginAccessResult(QqLoginAccessKind.Allowed,
                        "QQ 绑定已存在，群白名单验证通过。", status.Version, MaskQq(existing.Qq));
                }

                if (!IsWhitelisted(connection, transaction, normalizedQq!))
                    return new QqLoginAccessResult(QqLoginAccessKind.NotWhitelisted,
                        "该 QQ 不在当前群白名单内，无法绑定。", status.Version);
                EnsureAccountExists(connection, transaction, normalizedAccount);
                var now = Now();
                using var binding = connection.CreateCommand();
                binding.Transaction = transaction;
                binding.CommandText = """
                    INSERT INTO shared_account_qq_bindings(
                        account_key, qq, revision, bound_at, whitelist_version, updated_at, updated_by)
                    VALUES($accountKey, $qq, 1, $now, $version, $now, $updatedBy)
                    ON CONFLICT(account_key) DO UPDATE SET
                        qq=excluded.qq,
                        revision=shared_account_qq_bindings.revision + 1,
                        bound_at=excluded.bound_at,
                        whitelist_version=excluded.whitelist_version,
                        updated_at=excluded.updated_at,
                        updated_by=excluded.updated_by
                    WHERE shared_account_qq_bindings.qq IS NULL;
                    """;
                binding.Parameters.AddWithValue("$accountKey", normalizedAccount.ToUpperInvariant());
                binding.Parameters.AddWithValue("$qq", normalizedQq!);
                binding.Parameters.AddWithValue("$now", now);
                binding.Parameters.AddWithValue("$version", status.Version);
                binding.Parameters.AddWithValue("$updatedBy", normalizedAccount);
                try
                {
                    if (binding.ExecuteNonQuery() == 0)
                        return new QqLoginAccessResult(QqLoginAccessKind.QqAlreadyBound,
                            "该账号已经绑定 QQ，玩家不能自行更换绑定。", status.Version);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    return new QqLoginAccessResult(QqLoginAccessKind.QqAlreadyBound,
                        "该 QQ 已绑定其他账号，一个 QQ 只能绑定一个账号。", status.Version);
                }
                transaction.Commit();
                return new QqLoginAccessResult(QqLoginAccessKind.Allowed,
                    "QQ 绑定成功，群白名单验证通过。", status.Version, MaskQq(normalizedQq!));
            }
            finally { _gate.ExitWriteLock(); }
        }
        finally { _gate.ExitUpgradeableReadLock(); }
    }

    public QqLoginAccessResult CheckNewGameAccess(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            return CheckNewGameAccess(connection, transaction: null, normalizedAccount);
        }
        finally { _gate.ExitReadLock(); }
    }

    /// <summary>
    /// SQLite 立即事务跨进程锁住白名单写入窗口，资格复核与本进程房间注册线性化。
    /// 已注册房间不因随后发生的绑定变化被中断。
    /// </summary>
    public T ExecuteNewGameAdmission<T>(IEnumerable<string?> accounts, Func<T> registerGame)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registerGame);
        var normalizedAccounts = accounts
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeAccount(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedAccounts.Length == 0)
            throw new QqAccessDeniedException(NewGameDeniedMessage);

        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            foreach (var account in normalizedAccounts)
            {
                var access = CheckNewGameAccess(connection, transaction, account);
                if (!access.Allowed) throw new QqAccessDeniedException(NewGameDeniedMessage);
            }
            var result = registerGame();
            transaction.Commit();
            return result;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessDeniedException("共享账号资格正在更新，请稍后重试。");
        }
        finally { _gate.ExitReadLock(); }
    }

    public QqAccountBindingStatus GetAccountBindingStatus(string account)
    {
        var normalizedAccount = NormalizeAccount(account);
        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            var binding = ReadBinding(connection, transaction: null, normalizedAccount);
            return binding is null || binding.Qq is null
                ? new QqAccountBindingStatus(false, null, false, null, binding?.Revision ?? 0)
                : new QqAccountBindingStatus(true, MaskQq(binding.Qq),
                    IsWhitelisted(connection, transaction: null, binding.Qq),
                    binding.BoundAt, binding.Revision);
        }
        finally { _gate.ExitReadLock(); }
    }

    public IReadOnlyList<AdminQqAccountSummary> SearchAccountsForAdmin(
        string query,
        string searchBy,
        int limit = 20)
    {
        var normalizedSearchBy = string.Equals(searchBy, "qq", StringComparison.OrdinalIgnoreCase)
            ? "qq"
            : "player";
        var normalizedQuery = normalizedSearchBy == "qq"
            ? NormalizeQq(query)
            : NormalizeAdminQuery(query);
        _gate.EnterReadLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var command = connection.CreateCommand();
            if (normalizedSearchBy == "qq")
            {
                command.CommandText = """
                    SELECT a.account, a.display_name, a.created_at, a.last_login_at,
                           EXISTS(SELECT 1 FROM shared_player_credentials c WHERE c.account_key=a.account_key),
                           b.qq, b.bound_at, b.revision,
                           EXISTS(SELECT 1 FROM shared_qq_whitelist_members w WHERE w.qq=b.qq)
                    FROM shared_account_qq_bindings b
                    JOIN shared_accounts a ON a.account_key=b.account_key
                    WHERE b.qq=$qq
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue("$qq", normalizedQuery);
            }
            else
            {
                command.CommandText = """
                    SELECT a.account, a.display_name, a.created_at, a.last_login_at,
                           EXISTS(SELECT 1 FROM shared_player_credentials c WHERE c.account_key=a.account_key),
                           b.qq, b.bound_at, COALESCE(b.revision, 0),
                           EXISTS(SELECT 1 FROM shared_qq_whitelist_members w WHERE w.qq=b.qq)
                    FROM shared_accounts a
                    LEFT JOIN shared_account_qq_bindings b ON b.account_key=a.account_key
                    WHERE a.account_key LIKE $pattern ESCAPE '\' COLLATE NOCASE
                       OR a.display_name LIKE $pattern ESCAPE '\' COLLATE NOCASE
                    ORDER BY CASE
                               WHEN a.account_key=$exact COLLATE NOCASE THEN 0
                               WHEN a.display_name=$query COLLATE NOCASE THEN 1
                               ELSE 2
                             END,
                             a.last_login_at DESC,
                             a.account_key
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalizedQuery)}%");
                command.Parameters.AddWithValue("$exact", normalizedQuery.ToUpperInvariant());
                command.Parameters.AddWithValue("$query", normalizedQuery);
                command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, MaxAdminSearchResults));
            }

            var results = new List<AdminQqAccountSummary>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var resultAccount = reader.GetString(0);
                var displayName = reader.GetString(1);
                var qq = reader.IsDBNull(5) ? null : reader.GetString(5);
                var matchKind = normalizedSearchBy == "qq" ? "qq_exact"
                    : string.Equals(resultAccount, normalizedQuery, StringComparison.OrdinalIgnoreCase) ? "account_exact"
                    : string.Equals(displayName, normalizedQuery, StringComparison.OrdinalIgnoreCase) ? "nickname_exact"
                    : "fuzzy";
                results.Add(new AdminQqAccountSummary(
                    resultAccount,
                    displayName,
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4) != 0,
                    qq,
                    qq is null ? null : MaskQq(qq),
                    reader.GetInt64(8) != 0,
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.GetInt64(7),
                    matchKind));
            }
            return results;
        }
        finally { _gate.ExitReadLock(); }
    }

    public AdminQqBindingMutationResult AdminUpdateBinding(
        string adminAccount,
        string targetAccount,
        string action,
        string? qq,
        long expectedRevision,
        string requestId)
    {
        var normalizedAdmin = NormalizeAccount(adminAccount);
        var normalizedTarget = NormalizeAccount(targetAccount);
        var normalizedAction = action switch
        {
            "set" => "set",
            "unbind" => "unbind",
            _ => throw new QqAccessValidationException("不支持的 QQ 绑定操作。"),
        };
        if (expectedRevision < 0)
            throw new QqAccessValidationException("绑定版本无效，请重新搜索玩家后再试。");
        if (!Guid.TryParse(requestId, out var parsedRequestId))
            throw new QqAccessValidationException("请求标识无效，请刷新页面后重试。");
        var normalizedRequestId = parsedRequestId.ToString("D");
        var normalizedQq = normalizedAction == "set" ? NormalizeQq(qq) : null;
        var payloadHash = HashMutationPayload(normalizedTarget, normalizedAction, normalizedQq, expectedRevision);

        _gate.EnterWriteLock();
        try
        {
            using var connection = _accounts.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            var replay = ReadMutationReplay(connection, transaction, normalizedAdmin, normalizedRequestId);
            if (replay is not null)
            {
                if (!string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new QqAccessValidationException("同一请求标识对应了不同操作，已拒绝执行。");
                var replayIdentity = ReadAccountIdentity(connection, transaction, replay.TargetAccount)
                    ?? throw new QqAccessValidationException("原操作目标账号已不存在。");
                transaction.Commit();
                return new AdminQqBindingMutationResult(
                    replayIdentity.Account,
                    replayIdentity.DisplayName,
                    replay.Action == "set" ? normalizedQq : null,
                    replay.ResultingQqMasked,
                    replay.ResultingWhitelisted,
                    replay.Action == "set" ? replay.CreatedAt : null,
                    replay.ResultingRevision,
                    Replayed: true);
            }

            var identity = ReadAccountIdentity(connection, transaction, normalizedTarget)
                ?? throw new QqAccessValidationException("玩家账号不存在。");
            var status = ReadStatus(connection, transaction);
            if (!status.Initialized)
                throw new QqAccessValidationException("QQ 群白名单尚未初始化，不能修改绑定。");
            if (normalizedQq is not null && !IsWhitelisted(connection, transaction, normalizedQq))
                throw new QqAccessValidationException("目标 QQ 不在当前群白名单内，不能绑定。");

            var current = ReadBinding(connection, transaction, identity.Account);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRevision)
                throw new QqAccessValidationException("绑定已被其他管理员或登录流程更新，请重新搜索后再操作。");
            if (normalizedAction == "unbind" && current?.Qq is null)
                throw new QqAccessValidationException("该账号当前没有 QQ 绑定。");
            if (normalizedQq is not null && string.Equals(current?.Qq, normalizedQq, StringComparison.Ordinal))
                throw new QqAccessValidationException("新 QQ 与当前绑定相同。");

            var now = Now();
            var nextRevision = checked(currentRevision + 1);
            try
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    INSERT INTO shared_account_qq_bindings(
                        account_key, qq, revision, bound_at, whitelist_version, updated_at, updated_by)
                    VALUES($accountKey, $qq, $revision, $boundAt, $whitelistVersion, $updatedAt, $updatedBy)
                    ON CONFLICT(account_key) DO UPDATE SET
                        qq=excluded.qq,
                        revision=excluded.revision,
                        bound_at=excluded.bound_at,
                        whitelist_version=excluded.whitelist_version,
                        updated_at=excluded.updated_at,
                        updated_by=excluded.updated_by
                    WHERE shared_account_qq_bindings.revision=$expectedRevision;
                    """;
                update.Parameters.AddWithValue("$accountKey", identity.AccountKey);
                update.Parameters.AddWithValue("$qq", (object?)normalizedQq ?? DBNull.Value);
                update.Parameters.AddWithValue("$revision", nextRevision);
                update.Parameters.AddWithValue("$boundAt", normalizedQq is null ? DBNull.Value : now);
                update.Parameters.AddWithValue("$whitelistVersion", normalizedQq is null ? DBNull.Value : status.Version);
                update.Parameters.AddWithValue("$updatedAt", now);
                update.Parameters.AddWithValue("$updatedBy", normalizedAdmin);
                update.Parameters.AddWithValue("$expectedRevision", expectedRevision);
                if (update.ExecuteNonQuery() == 0)
                    throw new QqAccessValidationException("绑定已被并发更新，请重新搜索后再操作。");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new QqAccessValidationException("该 QQ 已绑定其他账号，一个 QQ 只能绑定一个账号。", ex);
            }

            using (var revokeSessions = connection.CreateCommand())
            {
                revokeSessions.Transaction = transaction;
                revokeSessions.CommandText =
                    "DELETE FROM shared_player_auth_sessions WHERE account_key=$accountKey;";
                revokeSessions.Parameters.AddWithValue("$accountKey", identity.AccountKey);
                revokeSessions.ExecuteNonQuery();
            }

            var detail = JsonSerializer.Serialize(new
            {
                previousQqMasked = current?.Qq is null ? null : MaskQq(current.Qq),
                newQqMasked = normalizedQq is null ? null : MaskQq(normalizedQq),
                previousRevision = currentRevision,
                newRevision = nextRevision,
            });
            AccountAuthenticationStore.InsertAdminAudit(
                connection, transaction, normalizedAdmin, identity.Account,
                normalizedAction == "set" ? "set_qq_binding" : "unbind_qq", normalizedRequestId, detail);
            AccountAuthenticationStore.InsertSecurityEvent(
                connection, transaction, "qq_binding_changed", identity.Account, nextRevision);

            using (var remember = connection.CreateCommand())
            {
                remember.Transaction = transaction;
                remember.CommandText = """
                    INSERT INTO shared_qq_binding_requests(
                        admin_account, request_id, payload_hash, target_account, action,
                        resulting_revision, resulting_qq_masked, resulting_whitelisted, created_at)
                    VALUES($admin, $requestId, $payloadHash, $target, $action,
                        $revision, $qqMasked, $whitelisted, $createdAt);
                    """;
                remember.Parameters.AddWithValue("$admin", normalizedAdmin);
                remember.Parameters.AddWithValue("$requestId", normalizedRequestId);
                remember.Parameters.AddWithValue("$payloadHash", payloadHash);
                remember.Parameters.AddWithValue("$target", identity.Account);
                remember.Parameters.AddWithValue("$action", normalizedAction);
                remember.Parameters.AddWithValue("$revision", nextRevision);
                remember.Parameters.AddWithValue("$qqMasked",
                    normalizedQq is null ? DBNull.Value : MaskQq(normalizedQq));
                remember.Parameters.AddWithValue("$whitelisted", normalizedQq is null ? 0 : 1);
                remember.Parameters.AddWithValue("$createdAt", now);
                remember.ExecuteNonQuery();
            }

            transaction.Commit();
            return new AdminQqBindingMutationResult(
                identity.Account, identity.DisplayName, normalizedQq,
                normalizedQq is null ? null : MaskQq(normalizedQq),
                normalizedQq is not null, normalizedQq is null ? null : now,
                nextRevision, Replayed: false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new QqAccessValidationException("共享账号数据库正忙，绑定没有改变，请稍后重试。", ex);
        }
        finally { _gate.ExitWriteLock(); }
    }

    public long GetLatestSecurityEventId()
    {
        using var connection = _accounts.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM shared_account_security_events;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public IReadOnlyList<AccountSecurityEvent> GetSecurityEventsAfter(long afterId, int limit = 100)
    {
        using var connection = _accounts.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_type, target_account, revision, created_at
            FROM shared_account_security_events
            WHERE id>$afterId ORDER BY id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$afterId", Math.Max(0, afterId));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var result = new List<AccountSecurityEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new AccountSecurityEvent(
                reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetInt64(4)));
        return result;
    }

    private QqLoginAccessResult CheckNewGameAccess(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string normalizedAccount)
    {
        var status = ReadStatus(connection, transaction);
        if (!status.Initialized)
            return new QqLoginAccessResult(QqLoginAccessKind.WhitelistUninitialized, NewGameDeniedMessage);
        var binding = ReadBinding(connection, transaction, normalizedAccount);
        if (binding?.Qq is null)
            return new QqLoginAccessResult(QqLoginAccessKind.NeedsBinding, NewGameDeniedMessage, status.Version);
        return IsWhitelisted(connection, transaction, binding.Qq)
            ? new QqLoginAccessResult(QqLoginAccessKind.Allowed,
                "QQ 群白名单验证通过。", status.Version, MaskQq(binding.Qq))
            : new QqLoginAccessResult(QqLoginAccessKind.NotWhitelisted,
                NewGameDeniedMessage, status.Version, MaskQq(binding.Qq));
    }

    private static MutationReplay? ReadMutationReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string adminAccount,
        string requestId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload_hash, target_account, action, resulting_revision,
                   resulting_qq_masked, resulting_whitelisted, created_at
            FROM shared_qq_binding_requests
            WHERE admin_account=$admin AND request_id=$requestId;
            """;
        command.Parameters.AddWithValue("$admin", adminAccount);
        command.Parameters.AddWithValue("$requestId", requestId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new MutationReplay(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5) != 0,
                reader.GetInt64(6))
            : null;
    }

    private static SharedAccountIdentity? ReadAccountIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT account_key, account, display_name
            FROM shared_accounts WHERE account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$accountKey", account.ToUpperInvariant());
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SharedAccountIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static void EnsureAccountExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM shared_accounts WHERE account_key=$accountKey;";
        command.Parameters.AddWithValue("$accountKey", account.ToUpperInvariant());
        if (command.ExecuteScalar() is null)
            throw new QqAccessValidationException("账号认证资料不存在，请重新登录。");
    }

    private static QqWhitelistImportResult ImportParsed(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string importedBy,
        ParsedImport parsed,
        bool initializationOnly)
    {
        var previousStatus = ReadStatus(connection, transaction);
        if (initializationOnly && previousStatus.Initialized)
            throw new QqAccessValidationException("首份白名单已经导入，请先按同一规则完成 QQ 绑定后再进入管理界面。");
        var previous = ReadWhitelist(connection, transaction);
        var added = parsed.QqNumbers.Except(previous, StringComparer.Ordinal).Count();
        var removed = previous.Except(parsed.QqNumbers, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var removedBoundCount = CountBoundQq(connection, transaction, removed);
        var version = previousStatus.Initialized ? checked(previousStatus.Version + 1) : 1;
        var importedAt = Now();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM shared_qq_whitelist_members;";
            clear.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO shared_qq_whitelist_members(qq, version) VALUES($qq, $version);";
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
                INSERT INTO shared_qq_whitelist_state(
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
            AddImportParameters(state, version, importedAt, importedBy,
                parsed.QqNumbers.Count, parsed.DuplicateCount, added, removed.Count, removedBoundCount);
            state.ExecuteNonQuery();
        }

        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO shared_qq_whitelist_import_audit(
                    version, imported_at, imported_by, member_count,
                    duplicate_count, added_count, removed_count, removed_bound_count)
                VALUES($version, $importedAt, $importedBy, $memberCount,
                    $duplicateCount, $addedCount, $removedCount, $removedBoundCount);
                """;
            AddImportParameters(audit, version, importedAt, importedBy,
                parsed.QqNumbers.Count, parsed.DuplicateCount, added, removed.Count, removedBoundCount);
            audit.ExecuteNonQuery();
        }

        if (removed.Count > 0)
        {
            using var revoke = connection.CreateCommand();
            revoke.Transaction = transaction;
            revoke.CommandText = """
                DELETE FROM shared_player_auth_sessions
                WHERE account_key IN (
                    SELECT account_key FROM shared_account_qq_bindings
                    WHERE qq IS NOT NULL
                      AND qq NOT IN (SELECT qq FROM shared_qq_whitelist_members)
                );
                """;
            revoke.ExecuteNonQuery();
        }
        AccountAuthenticationStore.InsertSecurityEvent(
            connection, transaction, "whitelist_replaced", targetAccount: null, version);
        return new QqWhitelistImportResult(version, importedAt, parsed.QqNumbers.Count,
            parsed.DuplicateCount, added, removed.Count, removedBoundCount);
    }

    private static QqWhitelistScheduledSyncRequest NormalizeScheduledSyncRequest(
        QqWhitelistScheduledSyncRequest request,
        string expectedGroupId,
        string expectedGroupName)
    {
        var normalizedExpectedGroupId = NormalizeQq(expectedGroupId);
        var normalizedGroupId = NormalizeQq(request.GroupId);
        if (!string.Equals(normalizedExpectedGroupId, normalizedGroupId, StringComparison.Ordinal))
            throw new QqAccessValidationException("QQ 群号与服务端授权目标不一致。");
        var normalizedExpectedGroupName = NormalizeSyncGroupName(expectedGroupName);
        var normalizedGroupName = NormalizeSyncGroupName(request.GroupName);
        if (!string.Equals(normalizedExpectedGroupName, normalizedGroupName, StringComparison.Ordinal))
            throw new QqAccessValidationException("QQ 群名与服务端授权目标不一致。");
        var operationKey = NormalizeSyncOperationKey(request.OperationKey);
        var expectedOperationKey = BuildScheduledSyncOperationKey(
            normalizedGroupId, request.ScheduledHour);
        if (!string.Equals(operationKey, expectedOperationKey, StringComparison.Ordinal))
            throw new QqAccessValidationException("整点同步幂等键格式无效。");
        if (request.ReportedMemberCount is < 1 or > MaxImportMembers)
            throw new QqAccessValidationException("QQ 群信息接口返回的成员数量无效。");
        return request with
        {
            OperationKey = operationKey,
            GroupId = normalizedGroupId,
            GroupName = normalizedGroupName,
            ClientInstanceId = NormalizeClientInstanceId(request.ClientInstanceId),
            MembersJson = request.MembersJson ?? throw new QqAccessValidationException("QQ 群成员快照缺失。"),
        };
    }

    public static string BuildScheduledSyncOperationKey(string groupId, long scheduledHour)
        => $"qq-whitelist:{NormalizeQq(groupId)}:{scheduledHour}";

    private static string NormalizeSyncOperationKey(string operationKey)
    {
        var normalized = (operationKey ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxSyncOperationKeyLength
            || normalized.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new QqAccessValidationException("整点同步幂等键无效。");
        return normalized;
    }

    private static string NormalizeSyncGroupName(string groupName)
    {
        var normalized = (groupName ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > MaxSyncGroupNameLength || normalized.Any(char.IsControl))
            throw new QqAccessValidationException("QQ 群名格式无效。");
        return normalized;
    }

    private static string NormalizeClientInstanceId(string clientInstanceId)
    {
        if (!Guid.TryParseExact((clientInstanceId ?? "").Trim(), "D", out var parsed))
            throw new QqAccessValidationException("机器人实例标识格式无效。");
        return parsed.ToString("D");
    }

    private static void ValidateFreshScheduledHour(
        long scheduledHour,
        long nowUnixSeconds,
        int maximumDelaySeconds)
    {
        DateTimeOffset scheduled;
        try { scheduled = DateTimeOffset.FromUnixTimeSeconds(scheduledHour); }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new QqAccessValidationException("整点时间超出有效范围。", ex);
        }
        var local = scheduled.ToOffset(TimeSpan.FromHours(8));
        if (local.Minute != 0 || local.Second != 0)
            throw new QqAccessValidationException("同步时间不是 UTC+8 的自然整点。");
        if (scheduledHour > nowUnixSeconds)
            throw new QqAccessValidationException("拒绝提前执行尚未到达的整点同步。");
        if (nowUnixSeconds - scheduledHour > maximumDelaySeconds)
            throw new QqAccessValidationException("整点同步请求已经过期，拒绝补发陈旧小时。");
    }

    private static string HashScheduledSyncRequest(
        QqWhitelistScheduledSyncRequest request,
        IReadOnlySet<string> members)
    {
        var canonical = new StringBuilder()
            .Append(request.OperationKey).Append('\n')
            .Append(request.ScheduledHour).Append('\n')
            .Append(request.GroupId).Append('\n')
            .Append(request.GroupName).Append('\n')
            .Append(request.ReportedMemberCount).Append('\n');
        foreach (var member in members.Order(StringComparer.Ordinal))
            canonical.Append(member).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static StoredScheduledSyncRun? ReadScheduledSyncRun(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string operationKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation_key, scheduled_hour, group_id, group_name, request_hash,
                   client_instance_id, version, imported_at, member_count, duplicate_count,
                   added_count, removed_count, removed_bound_count, notification_acked_at
            FROM shared_qq_whitelist_sync_runs WHERE operation_key=$operationKey;
            """;
        command.Parameters.AddWithValue("$operationKey", operationKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadScheduledSyncRun(reader) : null;
    }

    private static StoredScheduledSyncRun? ReadScheduledSyncRunByHour(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string groupId,
        long scheduledHour)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation_key, scheduled_hour, group_id, group_name, request_hash,
                   client_instance_id, version, imported_at, member_count, duplicate_count,
                   added_count, removed_count, removed_bound_count, notification_acked_at
            FROM shared_qq_whitelist_sync_runs
            WHERE group_id=$groupId AND scheduled_hour=$scheduledHour;
            """;
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$scheduledHour", scheduledHour);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadScheduledSyncRun(reader) : null;
    }

    private static StoredScheduledSyncRun ReadScheduledSyncRun(SqliteDataReader reader)
        => new(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            new QqWhitelistImportResult(
                reader.GetInt64(6), reader.GetInt64(7), reader.GetInt32(8),
                reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12)),
            reader.IsDBNull(13) ? null : reader.GetInt64(13));

    private static QqWhitelistScheduledSyncResult ToScheduledSyncResult(
        StoredScheduledSyncRun stored,
        string requestingClientInstanceId,
        bool replayed)
        => new(
            stored.OperationKey,
            stored.ScheduledHour,
            stored.GroupId,
            stored.GroupName,
            stored.ClientInstanceId,
            stored.Import,
            replayed,
            stored.NotificationAcknowledgedAt is null
                && string.Equals(
                    stored.ClientInstanceId, requestingClientInstanceId, StringComparison.Ordinal),
            stored.NotificationAcknowledgedAt);

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
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind != JsonValueKind.Array)
                    throw new QqAccessValidationException($"字段 {property.Name} 必须是数组。");
                return property.Value;
            }
        throw new QqAccessValidationException("JSON 对象缺少 members、data 或 list 成员数组。");
    }

    private static JsonElement ResolveQqValue(JsonElement item, int index)
    {
        if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number) return item;
        if (item.ValueKind != JsonValueKind.Object)
            throw new QqAccessValidationException($"第 {index} 条成员不是 QQ 字符串、数字或对象。");
        foreach (var fieldName in new[] { "qq", "uin", "user_id" })
            foreach (var property in item.EnumerateObject())
                if (string.Equals(property.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
        throw new QqAccessValidationException($"第 {index} 条成员对象缺少 qq、uin 或 user_id 字段。");
    }

    private static string ParseQqValue(JsonElement value, int index)
    {
        string candidate;
        if (value.ValueKind == JsonValueKind.String) candidate = value.GetString() ?? "";
        else if (value.ValueKind == JsonValueKind.Number)
        {
            candidate = value.GetRawText();
            if (!candidate.All(static character => character is >= '0' and <= '9'))
                throw new QqAccessValidationException($"第 {index} 条 QQ 数字必须是无小数、无指数的正整数。");
        }
        else throw new QqAccessValidationException($"第 {index} 条 QQ 字段必须是字符串或数字。");
        try { return NormalizeQq(candidate); }
        catch (QqAccessValidationException ex)
        {
            throw new QqAccessValidationException($"第 {index} 条成员无效：{ex.Message}");
        }
    }

    private static QqWhitelistStatus ReadStatus(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT version, member_count, imported_at, imported_by,
                   duplicate_count, added_count, removed_count, removed_bound_count
            FROM shared_qq_whitelist_state WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new QqWhitelistStatus(false, 0, 0, null, null, 0, 0, 0, 0);
        return new QqWhitelistStatus(true, reader.GetInt64(0), reader.GetInt32(1),
            reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
    }

    private static HashSet<string> ReadWhitelist(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT qq FROM shared_qq_whitelist_members;";
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
        command.CommandText = "SELECT qq FROM shared_account_qq_bindings WHERE qq IS NOT NULL;";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read()) if (removed.Contains(reader.GetString(0))) count++;
        return count;
    }

    private static StoredBinding? ReadBinding(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT qq, revision, bound_at
            FROM shared_account_qq_bindings WHERE account_key=$accountKey;
            """;
        command.Parameters.AddWithValue("$accountKey", account.ToUpperInvariant());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new StoredBinding(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    private static bool IsWhitelisted(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string qq)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM shared_qq_whitelist_members WHERE qq=$qq;";
        command.Parameters.AddWithValue("$qq", qq);
        return command.ExecuteScalar() is not null;
    }

    private static void AddImportParameters(
        SqliteCommand command, long version, long importedAt, string importedBy,
        int memberCount, int duplicateCount, int addedCount, int removedCount, int removedBoundCount)
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

    private static string NormalizeAdminQuery(string query)
    {
        var normalized = (query ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length is < 1 or > PlayerDataStore.MaxDisplayNameLength || normalized.Any(char.IsControl))
            throw new QqAccessValidationException($"请输入 1–{PlayerDataStore.MaxDisplayNameLength} 个字符搜索账号或昵称。");
        return normalized;
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string MaskQq(string qq)
    {
        if (qq.Length <= 5) return $"{qq[..1]}***{qq[^1..]}";
        return $"{qq[..3]}{new string('*', Math.Min(6, qq.Length - 5))}{qq[^2..]}";
    }

    private static string HashMutationPayload(string account, string action, string? qq, long revision)
    {
        var payload = $"{account.ToUpperInvariant()}\n{action}\n{qq ?? ""}\n{revision}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed record ParsedImport(HashSet<string> QqNumbers, int TotalCount, int DuplicateCount);
    private sealed record StoredBinding(string? Qq, long Revision, long? BoundAt);
    private sealed record MutationReplay(
        string PayloadHash,
        string TargetAccount,
        string Action,
        long ResultingRevision,
        string? ResultingQqMasked,
        bool ResultingWhitelisted,
        long CreatedAt);
    private sealed record SharedAccountIdentity(string AccountKey, string Account, string DisplayName);
    private sealed record StoredScheduledSyncRun(
        string OperationKey,
        long ScheduledHour,
        string GroupId,
        string GroupName,
        string RequestHash,
        string ClientInstanceId,
        QqWhitelistImportResult Import,
        long? NotificationAcknowledgedAt);

    [GeneratedRegex("^[0-9]{5,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex QqPattern();
}
