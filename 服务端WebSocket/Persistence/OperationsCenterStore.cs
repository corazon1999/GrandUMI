using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public static class OperationsCaseSources
{
    public const string PlayerReport = "player_report";
    public const string BugReport = "bug_report";
    public const string QqEvent = "qq_event";
    public const string Manual = "manual";
    public const string ConsistencyDoctor = "consistency_doctor";

    internal static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        PlayerReport, BugReport, QqEvent, Manual, ConsistencyDoctor,
    };
}

public static class OperationsPenaltyKinds
{
    public const string Mute = "mute";
    public const string MatchBan = "match_ban";
    public const string SpectateChatBan = "spectate_chat_ban";

    internal static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        Mute, MatchBan, SpectateChatBan,
    };
}

public sealed record OperationsCaseEvidenceInput(string Type, string PayloadJson, DateTime? ExpiresAtUtc = null);

public sealed record OperationsCaseCreate(
    string Source,
    string Category,
    string Title,
    string Description,
    string? ReporterAccount,
    string? SubjectAccount,
    string? RelatedAccount,
    string? RoomId,
    string? ReplayId,
    string? ExternalEventId,
    string? RequestId,
    IReadOnlyList<OperationsCaseEvidenceInput>? Evidence = null,
    string Priority = "normal");

public sealed record OperationsCaseQuery(
    string? Status = null,
    string? Source = null,
    string? Assignee = null,
    string? Account = null,
    int Offset = 0,
    int Limit = 50);

public sealed record OperationsCaseSummary(
    string CaseId,
    string Source,
    string Category,
    string Title,
    string Status,
    string Priority,
    string? ReporterAccount,
    string? SubjectAccount,
    string? RelatedAccount,
    string? RoomId,
    string? ReplayId,
    string? Assignee,
    string? Disposition,
    long CreatedAt,
    long? FirstActionAt,
    long UpdatedAt,
    int EvidenceCount,
    int ActivePenaltyCount);

public sealed record OperationsCaseEvidence(
    long Id,
    string Type,
    string PayloadJson,
    long CreatedAt,
    long? ExpiresAt);

public sealed record OperationsCaseEvent(
    long Id,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    string ActorAccount,
    string Source,
    string? RequestId,
    string Note,
    long CreatedAt);

public sealed record OperationsPenalty(
    string PenaltyId,
    string CaseId,
    string Account,
    string Kind,
    string Reason,
    string OperatorAccount,
    string Source,
    long StartsAt,
    long ExpiresAt,
    long? RevokedAt,
    string? RevokedBy,
    string? RevokeReason);

public sealed record OperationsCaseDetail(
    OperationsCaseSummary Summary,
    string Description,
    string? ExternalEventId,
    string? AppealText,
    IReadOnlyList<OperationsCaseEvidence> Evidence,
    IReadOnlyList<OperationsCaseEvent> Events,
    IReadOnlyList<OperationsPenalty> Penalties);

public sealed record OperationsCasePage(IReadOnlyList<OperationsCaseSummary> Items, int Total);

public sealed record OperationsCaseMetrics(
    int Total,
    int AwaitingFirstAction,
    long? FirstActionP90Milliseconds,
    IReadOnlyDictionary<string, int> ByStatus);

public sealed record OperationsRestrictions(
    bool Muted,
    bool MatchBanned,
    bool SpectateOrChatBanned,
    long? EarliestExpiry,
    IReadOnlyList<OperationsPenalty> ActivePenalties);

public sealed record PrivilegedAuditEntry(
    long Id,
    string ActorAccount,
    string Source,
    string Operation,
    string? Target,
    string RequestId,
    string Result,
    string DetailJson,
    long CreatedAt,
    string PreviousHash,
    string EventHash);

public sealed record HighRiskChallenge(
    string ChallengeId,
    string ConfirmationToken,
    string Operation,
    string Target,
    long ExpiresAt);

public sealed record ConsistencyFinding(
    long Id,
    string Scope,
    string FindingKey,
    string Status,
    string Severity,
    string AuthoritativeJson,
    string ObservedJson,
    string RepairAction,
    string? LastError,
    long FirstSeenAt,
    long LastSeenAt,
    long? ResolvedAt);

public sealed class OperationsCenterException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// 运营权威库：Case、证据、限时处罚、申诉、特权审计和高风险二次确认共用一个事务边界。
/// 聊天原文最多保留 30 天；处罚必须有到期时间；审计表由触发器禁止更新和删除，并用哈希链检测离线篡改。
/// </summary>
public sealed class OperationsCenterStore : IDisposable
{
    public const int ChatEvidenceRetentionDays = 30;
    public const int MaximumPenaltyDays = 365;
    public const int HighRiskChallengeMinutes = 10;

    private static readonly HashSet<string> CaseStatuses = new(StringComparer.Ordinal)
    {
        "new", "triaged", "investigating", "actioned", "resolved", "rejected", "appealed", "closed",
    };

    private static readonly Dictionary<string, HashSet<string>> CaseTransitions = new(StringComparer.Ordinal)
    {
        ["new"] = ["triaged", "rejected", "closed"],
        ["triaged"] = ["investigating", "actioned", "resolved", "rejected", "closed"],
        ["investigating"] = ["actioned", "resolved", "rejected", "closed"],
        ["actioned"] = ["resolved", "appealed", "closed"],
        ["resolved"] = ["appealed", "closed"],
        ["rejected"] = ["appealed", "closed"],
        ["appealed"] = ["investigating", "actioned", "resolved", "closed"],
        ["closed"] = [],
    };

    private static readonly HashSet<string> Priorities = new(StringComparer.Ordinal)
    {
        "low", "normal", "high", "critical",
    };

    private static readonly HashSet<string> HighRiskOperations = new(StringComparer.Ordinal)
    {
        "deploy_test", "deploy_production", "reset_password", "database_repair",
    };

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly string _connectionString;
    private int _initialized;
    private int _disposed;

    public OperationsCenterStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _initialized) != 0) return;
        lock (_gate)
        {
            if (Volatile.Read(ref _initialized) != 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=5000;

                CREATE TABLE IF NOT EXISTS operations_schema_migrations (
                    version       INTEGER PRIMARY KEY,
                    description   TEXT NOT NULL,
                    applied_at_ms INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS operations_cases (
                    case_id             TEXT PRIMARY KEY,
                    source              TEXT NOT NULL,
                    source_request_id   TEXT,
                    category            TEXT NOT NULL,
                    title               TEXT NOT NULL,
                    description         TEXT NOT NULL,
                    status              TEXT NOT NULL,
                    priority            TEXT NOT NULL,
                    reporter_account    TEXT COLLATE NOCASE,
                    subject_account     TEXT COLLATE NOCASE,
                    related_account     TEXT COLLATE NOCASE,
                    room_id             TEXT,
                    replay_id           TEXT,
                    external_event_id   TEXT,
                    assignee            TEXT COLLATE NOCASE,
                    disposition         TEXT,
                    appeal_text         TEXT,
                    created_at_ms       INTEGER NOT NULL,
                    first_action_at_ms  INTEGER,
                    updated_at_ms       INTEGER NOT NULL,
                    closed_at_ms        INTEGER,
                    UNIQUE(source, source_request_id)
                );
                CREATE INDEX IF NOT EXISTS ix_operations_cases_status_time
                    ON operations_cases(status, updated_at_ms DESC);
                CREATE INDEX IF NOT EXISTS ix_operations_cases_subject_time
                    ON operations_cases(subject_account, created_at_ms DESC);

                CREATE TABLE IF NOT EXISTS operations_case_evidence (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    case_id       TEXT NOT NULL REFERENCES operations_cases(case_id) ON DELETE CASCADE,
                    evidence_type TEXT NOT NULL,
                    payload_json  TEXT NOT NULL,
                    created_at_ms INTEGER NOT NULL,
                    expires_at_ms INTEGER
                );
                CREATE INDEX IF NOT EXISTS ix_operations_evidence_expiry
                    ON operations_case_evidence(expires_at_ms) WHERE expires_at_ms IS NOT NULL;

                CREATE TABLE IF NOT EXISTS operations_case_events (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    case_id        TEXT NOT NULL REFERENCES operations_cases(case_id) ON DELETE CASCADE,
                    event_type     TEXT NOT NULL,
                    from_status    TEXT,
                    to_status      TEXT,
                    actor_account  TEXT NOT NULL COLLATE NOCASE,
                    source         TEXT NOT NULL,
                    request_id     TEXT,
                    note           TEXT NOT NULL,
                    created_at_ms  INTEGER NOT NULL,
                    UNIQUE(actor_account, request_id)
                );
                CREATE TRIGGER IF NOT EXISTS operations_case_events_no_update
                BEFORE UPDATE ON operations_case_events BEGIN
                    SELECT RAISE(ABORT, 'operations case events are append-only');
                END;
                CREATE TRIGGER IF NOT EXISTS operations_case_events_no_delete
                BEFORE DELETE ON operations_case_events BEGIN
                    SELECT RAISE(ABORT, 'operations case events are append-only');
                END;

                CREATE TABLE IF NOT EXISTS operations_penalties (
                    penalty_id       TEXT PRIMARY KEY,
                    case_id          TEXT NOT NULL REFERENCES operations_cases(case_id),
                    account          TEXT NOT NULL COLLATE NOCASE,
                    kind             TEXT NOT NULL,
                    reason           TEXT NOT NULL,
                    operator_account TEXT NOT NULL COLLATE NOCASE,
                    source           TEXT NOT NULL,
                    request_id       TEXT NOT NULL,
                    starts_at_ms     INTEGER NOT NULL,
                    expires_at_ms    INTEGER NOT NULL,
                    revoked_at_ms    INTEGER,
                    revoked_by       TEXT COLLATE NOCASE,
                    revoke_reason    TEXT,
                    UNIQUE(operator_account, request_id),
                    CHECK(expires_at_ms > starts_at_ms)
                );
                CREATE INDEX IF NOT EXISTS ix_operations_penalties_account_expiry
                    ON operations_penalties(account, expires_at_ms DESC);

                CREATE TABLE IF NOT EXISTS privileged_audit_events (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    actor_account  TEXT NOT NULL COLLATE NOCASE,
                    source         TEXT NOT NULL,
                    operation      TEXT NOT NULL,
                    target         TEXT,
                    request_id     TEXT NOT NULL,
                    result         TEXT NOT NULL,
                    detail_json    TEXT NOT NULL,
                    created_at_ms  INTEGER NOT NULL,
                    previous_hash  TEXT NOT NULL,
                    event_hash     TEXT NOT NULL UNIQUE,
                    UNIQUE(actor_account, request_id)
                );
                CREATE TRIGGER IF NOT EXISTS privileged_audit_no_update
                BEFORE UPDATE ON privileged_audit_events BEGIN
                    SELECT RAISE(ABORT, 'privileged audit is append-only');
                END;
                CREATE TRIGGER IF NOT EXISTS privileged_audit_no_delete
                BEFORE DELETE ON privileged_audit_events BEGIN
                    SELECT RAISE(ABORT, 'privileged audit is append-only');
                END;

                CREATE TABLE IF NOT EXISTS high_risk_challenges (
                    challenge_id   TEXT PRIMARY KEY,
                    token_hash     TEXT NOT NULL,
                    actor_account  TEXT NOT NULL COLLATE NOCASE,
                    source         TEXT NOT NULL,
                    operation      TEXT NOT NULL,
                    target         TEXT NOT NULL,
                    request_id     TEXT NOT NULL,
                    created_at_ms  INTEGER NOT NULL,
                    expires_at_ms  INTEGER NOT NULL,
                    consumed_at_ms INTEGER,
                    UNIQUE(actor_account, request_id)
                );

                CREATE TABLE IF NOT EXISTS consistency_findings (
                    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    scope               TEXT NOT NULL,
                    finding_key         TEXT NOT NULL,
                    status              TEXT NOT NULL DEFAULT 'open',
                    severity            TEXT NOT NULL,
                    authoritative_json  TEXT NOT NULL,
                    observed_json       TEXT NOT NULL,
                    repair_action       TEXT NOT NULL,
                    last_error          TEXT,
                    first_seen_at_ms    INTEGER NOT NULL,
                    last_seen_at_ms     INTEGER NOT NULL,
                    resolved_at_ms      INTEGER,
                    UNIQUE(scope,finding_key),
                    CHECK(status IN ('open','queued','resolved','ignored'))
                );
                CREATE INDEX IF NOT EXISTS ix_consistency_findings_status
                    ON consistency_findings(status,severity,last_seen_at_ms DESC);

                INSERT OR IGNORE INTO operations_schema_migrations(version, description, applied_at_ms)
                VALUES(1, '统一 Case、处罚、特权审计、高风险确认与一致性巡检', unixepoch('now') * 1000);
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
            PurgeExpiredEvidenceCore(connection, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            using var stale = connection.CreateCommand();
            stale.CommandText = "DELETE FROM high_risk_challenges WHERE expires_at_ms < $cutoff OR consumed_at_ms IS NOT NULL;";
            stale.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds());
            stale.ExecuteNonQuery();
            Volatile.Write(ref _initialized, 1);
        }
    }

    public string CreateCase(OperationsCaseCreate input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureInitialized();
        var source = NormalizeSet(input.Source, OperationsCaseSources.All, "invalid_source", "Case 来源无效。");
        var category = RequiredText(input.Category, 1, 80, "Case 分类");
        var title = RequiredText(input.Title, 1, 160, "Case 标题");
        var description = RequiredText(input.Description, 1, 4_000, "Case 描述");
        var priority = NormalizeSet(input.Priority, Priorities, "invalid_priority", "Case 优先级无效。");
        var requestId = OptionalRequestId(input.RequestId);
        var reporterAccount = OptionalAccount(input.ReporterAccount);
        var subjectAccount = OptionalAccount(input.SubjectAccount);
        var relatedAccount = OptionalAccount(input.RelatedAccount);
        var roomId = OptionalText(input.RoomId, 100);
        var replayId = OptionalText(input.ReplayId, 100);
        var externalEventId = OptionalText(input.ExternalEventId, 160);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (requestId is not null)
            {
                using var existing = connection.CreateCommand();
                existing.Transaction = transaction;
                existing.CommandText = """
                    SELECT case_id,category,title,description,priority,reporter_account,subject_account,
                           related_account,room_id,replay_id,external_event_id
                    FROM operations_cases WHERE source=$source AND source_request_id=$requestId;
                    """;
                existing.Parameters.AddWithValue("$source", source);
                existing.Parameters.AddWithValue("$requestId", requestId);
                using var reader = existing.ExecuteReader();
                if (reader.Read())
                {
                    var replayed = reader.GetString(0);
                    var same = string.Equals(reader.GetString(1), category, StringComparison.Ordinal)
                        && string.Equals(reader.GetString(2), title, StringComparison.Ordinal)
                        && string.Equals(reader.GetString(3), description, StringComparison.Ordinal)
                        && string.Equals(reader.GetString(4), priority, StringComparison.Ordinal)
                        && NullableEquals(reader, 5, reporterAccount, account: true)
                        && NullableEquals(reader, 6, subjectAccount, account: true)
                        && NullableEquals(reader, 7, relatedAccount, account: true)
                        && NullableEquals(reader, 8, roomId)
                        && NullableEquals(reader, 9, replayId)
                        && NullableEquals(reader, 10, externalEventId);
                    if (!same)
                        throw new OperationsCenterException("request_conflict", "同一 requestId 已用于不同的 Case 创建请求。");
                    return replayed;
                }
            }

            var caseId = $"case-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}";
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO operations_cases(
                        case_id, source, source_request_id, category, title, description,
                        status, priority, reporter_account, subject_account, related_account,
                        room_id, replay_id, external_event_id, created_at_ms, updated_at_ms)
                    VALUES($caseId,$source,$requestId,$category,$title,$description,
                           'new',$priority,$reporter,$subject,$related,$room,$replay,$external,$now,$now);
                    """;
                insert.Parameters.AddWithValue("$caseId", caseId);
                insert.Parameters.AddWithValue("$source", source);
                insert.Parameters.AddWithValue("$requestId", Db(requestId));
                insert.Parameters.AddWithValue("$category", category);
                insert.Parameters.AddWithValue("$title", title);
                insert.Parameters.AddWithValue("$description", description);
                insert.Parameters.AddWithValue("$priority", priority);
                insert.Parameters.AddWithValue("$reporter", Db(reporterAccount));
                insert.Parameters.AddWithValue("$subject", Db(subjectAccount));
                insert.Parameters.AddWithValue("$related", Db(relatedAccount));
                insert.Parameters.AddWithValue("$room", Db(roomId));
                insert.Parameters.AddWithValue("$replay", Db(replayId));
                insert.Parameters.AddWithValue("$external", Db(externalEventId));
                insert.Parameters.AddWithValue("$now", now);
                insert.ExecuteNonQuery();
            }

            InsertCaseEvent(connection, transaction, caseId, "created", null, "new",
                reporterAccount ?? "system", source, null, "Case 已创建。", now);
            foreach (var evidence in input.Evidence ?? [])
                InsertEvidence(connection, transaction, caseId, evidence, now);
            transaction.Commit();
            return caseId;
        }
    }

    public OperationsCasePage ListCases(OperationsCaseQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureInitialized();
        var limit = Math.Clamp(query.Limit, 1, 100);
        var offset = Math.Clamp(query.Offset, 0, 100_000);
        lock (_gate)
        {
            using var connection = OpenConnection();
            PurgeExpiredEvidenceCore(connection, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var where = new List<string> { "1=1" };
            using var command = connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var status = NormalizeSet(query.Status, CaseStatuses, "invalid_status", "Case 状态无效。");
                where.Add("c.status=$status");
                command.Parameters.AddWithValue("$status", status);
            }
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                var source = NormalizeSet(query.Source, OperationsCaseSources.All, "invalid_source", "Case 来源无效。");
                where.Add("c.source=$source");
                command.Parameters.AddWithValue("$source", source);
            }
            if (!string.IsNullOrWhiteSpace(query.Assignee))
            {
                where.Add("c.assignee=$assignee COLLATE NOCASE");
                command.Parameters.AddWithValue("$assignee", RequiredText(query.Assignee, 1, 80, "责任人"));
            }
            if (!string.IsNullOrWhiteSpace(query.Account))
            {
                where.Add("(c.reporter_account=$account COLLATE NOCASE OR c.subject_account=$account COLLATE NOCASE OR c.related_account=$account COLLATE NOCASE)");
                command.Parameters.AddWithValue("$account", RequiredText(query.Account, 1, 80, "账号"));
            }
            var predicate = string.Join(" AND ", where);
            command.CommandText = $"""
                SELECT c.case_id,c.source,c.category,c.title,c.status,c.priority,
                       c.reporter_account,c.subject_account,c.related_account,c.room_id,c.replay_id,
                       c.assignee,c.disposition,c.created_at_ms,c.first_action_at_ms,c.updated_at_ms,
                       (SELECT COUNT(*) FROM operations_case_evidence e WHERE e.case_id=c.case_id),
                       (SELECT COUNT(*) FROM operations_penalties p WHERE p.case_id=c.case_id
                         AND p.revoked_at_ms IS NULL AND p.expires_at_ms>$now)
                FROM operations_cases c WHERE {predicate}
                ORDER BY CASE c.priority WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'normal' THEN 2 ELSE 3 END,
                         c.updated_at_ms DESC, c.case_id DESC
                LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var items = new List<OperationsCaseSummary>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) items.Add(ReadCaseSummary(reader));
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM operations_cases c WHERE {predicate};";
            CopyParameters(command, count, "$status", "$source", "$assignee", "$account");
            return new OperationsCasePage(items, Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
    }

    public OperationsCaseDetail GetCase(string caseId)
    {
        caseId = RequireCaseId(caseId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            PurgeExpiredEvidenceCore(connection, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return ReadCaseDetail(connection, caseId);
        }
    }

    public OperationsCaseDetail TransitionCase(
        string actorAccount,
        string source,
        string caseId,
        string toStatus,
        string? assignee,
        string? disposition,
        string note,
        string requestId)
    {
        actorAccount = RequireAccount(actorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        caseId = RequireCaseId(caseId);
        toStatus = NormalizeSet(toStatus, CaseStatuses, "invalid_status", "Case 状态无效。");
        note = OptionalText(note, 2_000) ?? "";
        requestId = RequireRequestId(requestId);
        var normalizedAssignee = OptionalAccount(assignee);
        var normalizedDisposition = OptionalText(disposition, 2_000);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (TryReadCaseMutation(
                    connection, transaction, actorAccount, requestId, caseId, "status_changed", toStatus))
                return ReadCaseDetail(connection, caseId, transaction);
            string current;
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT status FROM operations_cases WHERE case_id=$caseId;";
                read.Parameters.AddWithValue("$caseId", caseId);
                current = read.ExecuteScalar() as string
                    ?? throw new OperationsCenterException("not_found", "Case 不存在。");
            }
            if (!string.Equals(current, toStatus, StringComparison.Ordinal)
                && !CaseTransitions[current].Contains(toStatus))
                throw new OperationsCenterException("invalid_transition", $"Case 不能从 {current} 变更为 {toStatus}。");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE operations_cases
                    SET status=$status,
                        assignee=COALESCE($assignee,assignee),
                        disposition=COALESCE($disposition,disposition),
                        first_action_at_ms=COALESCE(first_action_at_ms,$now),
                        closed_at_ms=CASE WHEN $status='closed' THEN $now ELSE NULL END,
                        updated_at_ms=$now
                    WHERE case_id=$caseId;
                    """;
                update.Parameters.AddWithValue("$status", toStatus);
                update.Parameters.AddWithValue("$assignee", Db(normalizedAssignee));
                update.Parameters.AddWithValue("$disposition", Db(normalizedDisposition));
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$caseId", caseId);
                update.ExecuteNonQuery();
            }
            InsertCaseEvent(connection, transaction, caseId, "status_changed", current, toStatus,
                actorAccount, source, requestId, note, now);
            AppendAuditCore(connection, transaction, actorAccount, source, "case_transition", caseId,
                requestId, "success", JsonSerializer.Serialize(new { from = current, to = toStatus, assignee = normalizedAssignee, disposition = normalizedDisposition }), now);
            transaction.Commit();
            return ReadCaseDetail(connection, caseId);
        }
    }

    public OperationsCaseDetail SubmitAppeal(string account, string caseId, string appealText, string requestId)
    {
        account = RequireAccount(account);
        caseId = RequireCaseId(caseId);
        appealText = RequiredText(appealText, 10, 2_000, "申诉说明");
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (TryReadCaseMutation(
                    connection, transaction, account, requestId, caseId, "appeal_submitted", "appealed"))
                return ReadCaseDetail(connection, caseId, transaction);
            string status;
            string? subject;
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT status,subject_account FROM operations_cases WHERE case_id=$caseId;";
                read.Parameters.AddWithValue("$caseId", caseId);
                using var reader = read.ExecuteReader();
                if (!reader.Read()) throw new OperationsCenterException("not_found", "Case 不存在。");
                status = reader.GetString(0);
                subject = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
            if (!string.Equals(subject, account, StringComparison.OrdinalIgnoreCase))
                throw new OperationsCenterException("forbidden", "只能对与当前账号相关的处罚 Case 提交申诉。");
            if (!CaseTransitions.GetValueOrDefault(status, []).Contains("appealed"))
                throw new OperationsCenterException("invalid_transition", "当前 Case 状态不能提交申诉。");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE operations_cases SET status='appealed',appeal_text=$appeal,updated_at_ms=$now,closed_at_ms=NULL WHERE case_id=$caseId;";
                update.Parameters.AddWithValue("$appeal", appealText);
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$caseId", caseId);
                update.ExecuteNonQuery();
            }
            InsertCaseEvent(connection, transaction, caseId, "appeal_submitted", status, "appealed",
                account, "player", requestId, appealText, now);
            transaction.Commit();
            return ReadCaseDetail(connection, caseId);
        }
    }

    public OperationsPenalty ApplyPenalty(
        string operatorAccount,
        string source,
        string caseId,
        string account,
        string kind,
        DateTime expiresAtUtc,
        string reason,
        string requestId)
    {
        operatorAccount = RequireAccount(operatorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        caseId = RequireCaseId(caseId);
        account = RequireAccount(account);
        kind = NormalizeSet(kind, OperationsPenaltyKinds.All, "invalid_penalty", "处罚类型无效。");
        reason = RequiredText(reason, 3, 1_000, "处罚原因");
        requestId = RequireRequestId(requestId);
        var now = DateTimeOffset.UtcNow;
        var expires = expiresAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)
            : expiresAtUtc.ToUniversalTime();
        if (expires <= now.UtcDateTime.AddMinutes(1) || expires > now.UtcDateTime.AddDays(MaximumPenaltyDays))
            throw new OperationsCenterException("invalid_expiry", $"处罚必须在 1 分钟后到期，且最长不能超过 {MaximumPenaltyDays} 天；不支持永久处罚。");
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var replay = connection.CreateCommand())
            {
                replay.Transaction = transaction;
                replay.CommandText = "SELECT penalty_id FROM operations_penalties WHERE operator_account=$actor AND request_id=$requestId;";
                replay.Parameters.AddWithValue("$actor", operatorAccount);
                replay.Parameters.AddWithValue("$requestId", requestId);
                if (replay.ExecuteScalar() is string replayed)
                {
                    var existingPenalty = ReadPenalty(connection, replayed, transaction);
                    var expectedExpiry = new DateTimeOffset(expires).ToUnixTimeMilliseconds();
                    if (!string.Equals(existingPenalty.CaseId, caseId, StringComparison.Ordinal)
                        || !string.Equals(existingPenalty.Account, account, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(existingPenalty.Kind, kind, StringComparison.Ordinal)
                        || !string.Equals(existingPenalty.Reason, reason, StringComparison.Ordinal)
                        || existingPenalty.ExpiresAt != expectedExpiry)
                        throw new OperationsCenterException(
                            "request_conflict", "同一 requestId 已用于不同的处罚请求。");
                    return existingPenalty;
                }
            }
            using (var caseCheck = connection.CreateCommand())
            {
                caseCheck.Transaction = transaction;
                caseCheck.CommandText = "SELECT 1 FROM operations_cases WHERE case_id=$caseId;";
                caseCheck.Parameters.AddWithValue("$caseId", caseId);
                if (caseCheck.ExecuteScalar() is null)
                    throw new OperationsCenterException("not_found", "处罚必须关联已存在的 Case。");
            }
            var penaltyId = $"penalty-{Guid.NewGuid():N}";
            var startsAt = now.ToUnixTimeMilliseconds();
            var expiresAt = new DateTimeOffset(expires).ToUnixTimeMilliseconds();
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO operations_penalties(
                        penalty_id,case_id,account,kind,reason,operator_account,source,request_id,starts_at_ms,expires_at_ms)
                    VALUES($id,$caseId,$account,$kind,$reason,$operator,$source,$requestId,$starts,$expires);
                    """;
                insert.Parameters.AddWithValue("$id", penaltyId);
                insert.Parameters.AddWithValue("$caseId", caseId);
                insert.Parameters.AddWithValue("$account", account);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$reason", reason);
                insert.Parameters.AddWithValue("$operator", operatorAccount);
                insert.Parameters.AddWithValue("$source", source);
                insert.Parameters.AddWithValue("$requestId", requestId);
                insert.Parameters.AddWithValue("$starts", startsAt);
                insert.Parameters.AddWithValue("$expires", expiresAt);
                insert.ExecuteNonQuery();
            }
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE operations_cases SET status='actioned',
                        first_action_at_ms=COALESCE(first_action_at_ms,$now),updated_at_ms=$now
                    WHERE case_id=$caseId AND status<>'closed';
                    """;
                update.Parameters.AddWithValue("$now", startsAt);
                update.Parameters.AddWithValue("$caseId", caseId);
                update.ExecuteNonQuery();
            }
            InsertCaseEvent(connection, transaction, caseId, "penalty_applied", null, "actioned",
                operatorAccount, source, null, $"{kind}，到期 {expires:O}：{reason}", startsAt);
            AppendAuditCore(connection, transaction, operatorAccount, source, "penalty_apply", account,
                requestId, "success", JsonSerializer.Serialize(new { caseId, penaltyId, kind, expiresAt }), startsAt);
            transaction.Commit();
            return ReadPenalty(connection, penaltyId);
        }
    }

    public OperationsPenalty RevokePenalty(
        string operatorAccount,
        string source,
        string penaltyId,
        string reason,
        string requestId)
    {
        operatorAccount = RequireAccount(operatorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        penaltyId = RequiredText(penaltyId, 8, 100, "处罚 ID");
        reason = RequiredText(reason, 3, 1_000, "撤销原因");
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (AuditRequestExists(connection, transaction, operatorAccount, requestId, "penalty_revoke", penaltyId))
                return ReadPenalty(connection, penaltyId, transaction);
            var penalty = ReadPenalty(connection, penaltyId, transaction);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE operations_penalties SET revoked_at_ms=$now,revoked_by=$actor,revoke_reason=$reason
                    WHERE penalty_id=$id AND revoked_at_ms IS NULL;
                    """;
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$actor", operatorAccount);
                update.Parameters.AddWithValue("$reason", reason);
                update.Parameters.AddWithValue("$id", penaltyId);
                update.ExecuteNonQuery();
            }
            InsertCaseEvent(connection, transaction, penalty.CaseId, "penalty_revoked", null, null,
                operatorAccount, source, null, $"{penalty.Kind}：{reason}", now);
            AppendAuditCore(connection, transaction, operatorAccount, source, "penalty_revoke", penaltyId,
                requestId, "success", JsonSerializer.Serialize(new { penalty.CaseId, penalty.Account, reason }), now);
            transaction.Commit();
            return ReadPenalty(connection, penaltyId);
        }
    }

    public OperationsRestrictions GetRestrictions(string account, DateTime? nowUtc = null)
    {
        account = RequireAccount(account);
        EnsureInitialized();
        var now = new DateTimeOffset((nowUtc ?? DateTime.UtcNow).ToUniversalTime()).ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT penalty_id,case_id,account,kind,reason,operator_account,source,
                       starts_at_ms,expires_at_ms,revoked_at_ms,revoked_by,revoke_reason
                FROM operations_penalties
                WHERE account=$account COLLATE NOCASE AND revoked_at_ms IS NULL AND expires_at_ms>$now
                ORDER BY expires_at_ms,penalty_id;
                """;
            command.Parameters.AddWithValue("$account", account);
            command.Parameters.AddWithValue("$now", now);
            var active = new List<OperationsPenalty>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) active.Add(ReadPenalty(reader));
            return new OperationsRestrictions(
                active.Any(item => item.Kind == OperationsPenaltyKinds.Mute),
                active.Any(item => item.Kind == OperationsPenaltyKinds.MatchBan),
                active.Any(item => item.Kind == OperationsPenaltyKinds.SpectateChatBan),
                active.Count == 0 ? null : active.Min(item => item.ExpiresAt),
                active);
        }
    }

    public HighRiskChallenge IssueHighRiskChallenge(
        string actorAccount,
        string source,
        string operation,
        string target,
        string requestId)
    {
        actorAccount = RequireAccount(actorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        if (!string.Equals(source, "web_admin", StringComparison.Ordinal))
            throw new OperationsCenterException("forbidden_source", "高风险确认只能从已认证的网页管理工作台发起；QQ/Agent 无权申请。");
        operation = NormalizeSet(operation, HighRiskOperations, "invalid_operation", "不支持的高风险操作。");
        target = RequiredText(target, 1, 160, "操作目标");
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        var challengeId = $"challenge-{Guid.NewGuid():N}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(HighRiskChallengeMinutes).ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var duplicate = connection.CreateCommand())
            {
                duplicate.Transaction = transaction;
                duplicate.CommandText = "SELECT 1 FROM high_risk_challenges WHERE actor_account=$actor AND request_id=$requestId;";
                duplicate.Parameters.AddWithValue("$actor", actorAccount);
                duplicate.Parameters.AddWithValue("$requestId", requestId);
                if (duplicate.ExecuteScalar() is not null)
                    throw new OperationsCenterException("request_replayed", "确认凭证只显示一次；请使用已取得的凭证或重新发起新的确认请求。");
            }
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO high_risk_challenges(
                        challenge_id,token_hash,actor_account,source,operation,target,request_id,created_at_ms,expires_at_ms)
                    VALUES($id,$hash,$actor,$source,$operation,$target,$requestId,$now,$expires);
                    """;
                insert.Parameters.AddWithValue("$id", challengeId);
                insert.Parameters.AddWithValue("$hash", HashToken(token));
                insert.Parameters.AddWithValue("$actor", actorAccount);
                insert.Parameters.AddWithValue("$source", source);
                insert.Parameters.AddWithValue("$operation", operation);
                insert.Parameters.AddWithValue("$target", target);
                insert.Parameters.AddWithValue("$requestId", requestId);
                insert.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
                insert.Parameters.AddWithValue("$expires", expiresAt);
                insert.ExecuteNonQuery();
            }
            AppendAuditCore(connection, transaction, actorAccount, source, "high_risk_challenge", target,
                requestId, "pending", JsonSerializer.Serialize(new { operation, challengeId, expiresAt }), now.ToUnixTimeMilliseconds());
            transaction.Commit();
        }
        return new HighRiskChallenge(challengeId, token, operation, target, expiresAt);
    }

    public void ConsumeHighRiskChallenge(
        string actorAccount,
        string source,
        string operation,
        string target,
        string challengeId,
        string confirmationToken)
    {
        actorAccount = RequireAccount(actorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        if (!string.Equals(source, "web_admin", StringComparison.Ordinal))
            throw new OperationsCenterException("forbidden_source", "QQ/Agent 不能确认高风险操作。");
        operation = NormalizeSet(operation, HighRiskOperations, "invalid_operation", "不支持的高风险操作。");
        target = RequiredText(target, 1, 160, "操作目标");
        challengeId = RequiredText(challengeId, 12, 100, "确认 ID");
        confirmationToken = RequiredText(confirmationToken, 32, 100, "确认凭证");
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE high_risk_challenges SET consumed_at_ms=$now
                WHERE challenge_id=$id AND actor_account=$actor COLLATE NOCASE AND source=$source
                  AND operation=$operation AND target=$target AND token_hash=$hash
                  AND consumed_at_ms IS NULL AND expires_at_ms >= $now;
                """;
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            update.Parameters.AddWithValue("$id", challengeId);
            update.Parameters.AddWithValue("$actor", actorAccount);
            update.Parameters.AddWithValue("$source", source);
            update.Parameters.AddWithValue("$operation", operation);
            update.Parameters.AddWithValue("$target", target);
            update.Parameters.AddWithValue("$hash", HashToken(confirmationToken));
            if (update.ExecuteNonQuery() != 1)
                throw new OperationsCenterException("confirmation_invalid", "确认凭证无效、已使用、已过期或与操作目标不匹配。");
            transaction.Commit();
        }
    }

    public void AppendPrivilegedAudit(
        string actorAccount,
        string source,
        string operation,
        string? target,
        string requestId,
        string result,
        string detailJson)
    {
        actorAccount = RequireAccount(actorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        operation = RequiredText(operation, 1, 100, "操作名");
        target = OptionalText(target, 160);
        requestId = RequireRequestId(requestId);
        result = RequiredText(result, 1, 40, "操作结果");
        detailJson = NormalizeJson(detailJson, 8_000);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (AuditRequestExists(connection, transaction, actorAccount, requestId, operation, target)) return;
            AppendAuditCore(connection, transaction, actorAccount, source, operation, target, requestId,
                result, detailJson, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            transaction.Commit();
        }
    }

    public IReadOnlyList<PrivilegedAuditEntry> ListPrivilegedAudit(int offset = 0, int limit = 100)
    {
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id,actor_account,source,operation,target,request_id,result,detail_json,
                       created_at_ms,previous_hash,event_hash
                FROM privileged_audit_events ORDER BY id DESC LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            command.Parameters.AddWithValue("$offset", Math.Clamp(offset, 0, 100_000));
            var result = new List<PrivilegedAuditEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadAudit(reader));
            return result;
        }
    }

    public bool VerifyAuditChain()
    {
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id,actor_account,source,operation,target,request_id,result,detail_json,
                       created_at_ms,previous_hash,event_hash
                FROM privileged_audit_events ORDER BY id;
                """;
            var previous = new string('0', 64);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = ReadAudit(reader);
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(previous), Encoding.ASCII.GetBytes(entry.PreviousHash))) return false;
                var expected = AuditHash(previous, entry.ActorAccount, entry.Source, entry.Operation,
                    entry.Target, entry.RequestId, entry.Result, entry.DetailJson, entry.CreatedAt);
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(entry.EventHash))) return false;
                previous = entry.EventHash;
            }
            return true;
        }
    }

    public OperationsCaseMetrics GetCaseMetrics(DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        EnsureInitialized();
        var from = new DateTimeOffset((fromUtc ?? DateTime.UtcNow.AddDays(-30)).ToUniversalTime()).ToUnixTimeMilliseconds();
        var to = new DateTimeOffset((toUtc ?? DateTime.UtcNow).ToUniversalTime()).ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT status,created_at_ms,first_action_at_ms
                FROM operations_cases WHERE created_at_ms >= $from AND created_at_ms <= $to;
                """;
            command.Parameters.AddWithValue("$from", from);
            command.Parameters.AddWithValue("$to", to);
            var byStatus = new Dictionary<string, int>(StringComparer.Ordinal);
            var durations = new List<long>();
            var awaiting = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var status = reader.GetString(0);
                byStatus[status] = byStatus.GetValueOrDefault(status) + 1;
                if (reader.IsDBNull(2)) awaiting++;
                else durations.Add(Math.Max(0, reader.GetInt64(2) - reader.GetInt64(1)));
            }
            durations.Sort();
            long? p90 = durations.Count == 0
                ? null
                : durations[Math.Max(0, (int)Math.Ceiling(durations.Count * 0.9) - 1)];
            return new OperationsCaseMetrics(byStatus.Values.Sum(), awaiting, p90, byStatus);
        }
    }

    public long UpsertConsistencyFinding(
        string scope,
        string findingKey,
        string severity,
        string authoritativeJson,
        string observedJson,
        string repairAction,
        string? lastError = null)
    {
        scope = RequiredText(scope, 1, 80, "巡检范围");
        findingKey = RequiredText(findingKey, 1, 200, "差异键");
        severity = NormalizeSet(severity, new HashSet<string>(["info", "warning", "critical"], StringComparer.Ordinal),
            "invalid_severity", "差异级别无效。");
        authoritativeJson = NormalizeJson(authoritativeJson, 16_000);
        observedJson = NormalizeJson(observedJson, 16_000);
        repairAction = RequiredText(repairAction, 1, 100, "修复动作");
        lastError = OptionalText(lastError, 1_000);
        EnsureInitialized();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO consistency_findings(
                    scope,finding_key,status,severity,authoritative_json,observed_json,
                    repair_action,last_error,first_seen_at_ms,last_seen_at_ms)
                VALUES($scope,$key,'open',$severity,$authority,$observed,$repair,$error,$now,$now)
                ON CONFLICT(scope,finding_key) DO UPDATE SET
                    status=CASE WHEN consistency_findings.status='queued' THEN 'queued' ELSE 'open' END,
                    severity=excluded.severity,
                    authoritative_json=excluded.authoritative_json,
                    observed_json=excluded.observed_json,
                    repair_action=excluded.repair_action,
                    last_error=excluded.last_error,
                    last_seen_at_ms=excluded.last_seen_at_ms,
                    resolved_at_ms=NULL
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", findingKey);
            command.Parameters.AddWithValue("$severity", severity);
            command.Parameters.AddWithValue("$authority", authoritativeJson);
            command.Parameters.AddWithValue("$observed", observedJson);
            command.Parameters.AddWithValue("$repair", repairAction);
            command.Parameters.AddWithValue("$error", Db(lastError));
            command.Parameters.AddWithValue("$now", now);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public void ResolveConsistencyFinding(string scope, string findingKey)
    {
        scope = RequiredText(scope, 1, 80, "巡检范围");
        findingKey = RequiredText(findingKey, 1, 200, "差异键");
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE consistency_findings SET status='resolved',resolved_at_ms=$now,last_seen_at_ms=$now,last_error=NULL
                WHERE scope=$scope AND finding_key=$key AND status<>'resolved';
                """;
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", findingKey);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
    }

    public void MarkConsistencyRepairQueued(
        long findingId,
        string actorAccount,
        string source,
        string requestId)
    {
        actorAccount = RequireAccount(actorAccount);
        source = RequiredText(source, 1, 40, "操作来源");
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (AuditRequestExists(connection, transaction, actorAccount, requestId, "consistency_repair_queue", findingId.ToString(CultureInfo.InvariantCulture))) return;
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE consistency_findings SET status='queued',last_seen_at_ms=$now WHERE id=$id AND status IN ('open','queued');";
                update.Parameters.AddWithValue("$id", findingId);
                update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (update.ExecuteNonQuery() == 0)
                    throw new OperationsCenterException("not_found", "一致性差异不存在或已经结束。");
            }
            AppendAuditCore(connection, transaction, actorAccount, source, "consistency_repair_queue",
                findingId.ToString(CultureInfo.InvariantCulture), requestId, "success", "{}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            transaction.Commit();
        }
    }

    public IReadOnlyList<ConsistencyFinding> ListConsistencyFindings(string? status = null, int limit = 200)
    {
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = string.IsNullOrWhiteSpace(status)
                ? """
                    SELECT id,scope,finding_key,status,severity,authoritative_json,observed_json,
                           repair_action,last_error,first_seen_at_ms,last_seen_at_ms,resolved_at_ms
                    FROM consistency_findings ORDER BY CASE severity WHEN 'critical' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END,last_seen_at_ms DESC LIMIT $limit;
                    """
                : """
                    SELECT id,scope,finding_key,status,severity,authoritative_json,observed_json,
                           repair_action,last_error,first_seen_at_ms,last_seen_at_ms,resolved_at_ms
                    FROM consistency_findings WHERE status=$status
                    ORDER BY CASE severity WHEN 'critical' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END,last_seen_at_ms DESC LIMIT $limit;
                    """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            if (!string.IsNullOrWhiteSpace(status))
                command.Parameters.AddWithValue("$status", RequiredText(status, 1, 20, "差异状态"));
            var result = new List<ConsistencyFinding>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(new ConsistencyFinding(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), NullableString(reader, 8),
                reader.GetInt64(9), reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt64(11)));
            return result;
        }
    }

    public int PurgeExpiredEvidence(DateTime? nowUtc = null)
    {
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            return PurgeExpiredEvidenceCore(connection,
                new DateTimeOffset((nowUtc ?? DateTime.UtcNow).ToUniversalTime()).ToUnixTimeMilliseconds());
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SqliteConnection.ClearAllPools();
    }

    private static void InsertEvidence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        OperationsCaseEvidenceInput evidence,
        long now)
    {
        var type = RequiredText(evidence.Type, 1, 80, "证据类型");
        var payload = NormalizeJson(evidence.PayloadJson, 64_000);
        long? expiresAt = evidence.ExpiresAtUtc is { } explicitExpiry
            ? new DateTimeOffset(explicitExpiry.ToUniversalTime()).ToUnixTimeMilliseconds()
            : null;
        if (string.Equals(type, "game_chat", StringComparison.Ordinal))
        {
            var maximum = DateTimeOffset.FromUnixTimeMilliseconds(now)
                .AddDays(ChatEvidenceRetentionDays).ToUnixTimeMilliseconds();
            expiresAt = Math.Min(expiresAt ?? maximum, maximum);
        }
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO operations_case_evidence(case_id,evidence_type,payload_json,created_at_ms,expires_at_ms)
            VALUES($caseId,$type,$payload,$now,$expires);
            """;
        insert.Parameters.AddWithValue("$caseId", caseId);
        insert.Parameters.AddWithValue("$type", type);
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$now", now);
        insert.Parameters.AddWithValue("$expires", expiresAt is null ? DBNull.Value : expiresAt.Value);
        insert.ExecuteNonQuery();
    }

    private static int PurgeExpiredEvidenceCore(SqliteConnection connection, long now)
    {
        using var purge = connection.CreateCommand();
        purge.CommandText = "DELETE FROM operations_case_evidence WHERE expires_at_ms IS NOT NULL AND expires_at_ms <= $now;";
        purge.Parameters.AddWithValue("$now", now);
        return purge.ExecuteNonQuery();
    }

    private static void InsertCaseEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string eventType,
        string? fromStatus,
        string? toStatus,
        string actor,
        string source,
        string? requestId,
        string note,
        long now)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO operations_case_events(
                case_id,event_type,from_status,to_status,actor_account,source,request_id,note,created_at_ms)
            VALUES($caseId,$eventType,$from,$to,$actor,$source,$requestId,$note,$now);
            """;
        insert.Parameters.AddWithValue("$caseId", caseId);
        insert.Parameters.AddWithValue("$eventType", eventType);
        insert.Parameters.AddWithValue("$from", Db(fromStatus));
        insert.Parameters.AddWithValue("$to", Db(toStatus));
        insert.Parameters.AddWithValue("$actor", actor);
        insert.Parameters.AddWithValue("$source", source);
        insert.Parameters.AddWithValue("$requestId", Db(requestId));
        insert.Parameters.AddWithValue("$note", note);
        insert.Parameters.AddWithValue("$now", now);
        insert.ExecuteNonQuery();
    }

    private static bool TryReadCaseMutation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string requestId,
        string expectedCaseId,
        string expectedEventType,
        string? expectedToStatus)
    {
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT case_id,event_type,to_status FROM operations_case_events WHERE actor_account=$actor AND request_id=$requestId;";
        read.Parameters.AddWithValue("$actor", actor);
        read.Parameters.AddWithValue("$requestId", requestId);
        using var reader = read.ExecuteReader();
        if (!reader.Read()) return false;
        var existingCaseId = reader.GetString(0);
        var eventType = reader.GetString(1);
        var toStatus = reader.IsDBNull(2) ? null : reader.GetString(2);
        if (!string.Equals(existingCaseId, expectedCaseId, StringComparison.Ordinal)
            || !string.Equals(eventType, expectedEventType, StringComparison.Ordinal)
            || !string.Equals(toStatus, expectedToStatus, StringComparison.Ordinal))
            throw new OperationsCenterException("request_conflict", "同一 requestId 已用于不同的 Case 操作。");
        return true;
    }

    private static bool NullableEquals(SqliteDataReader reader, int ordinal, string? expected, bool account = false)
    {
        var actual = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return string.Equals(actual, expected,
            account ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void AppendAuditCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string source,
        string operation,
        string? target,
        string requestId,
        string result,
        string detailJson,
        long now)
    {
        string previous;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT event_hash FROM privileged_audit_events ORDER BY id DESC LIMIT 1;";
            previous = read.ExecuteScalar() as string ?? new string('0', 64);
        }
        var hash = AuditHash(previous, actor, source, operation, target, requestId, result, detailJson, now);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO privileged_audit_events(
                actor_account,source,operation,target,request_id,result,detail_json,created_at_ms,previous_hash,event_hash)
            VALUES($actor,$source,$operation,$target,$requestId,$result,$detail,$now,$previous,$hash);
            """;
        insert.Parameters.AddWithValue("$actor", actor);
        insert.Parameters.AddWithValue("$source", source);
        insert.Parameters.AddWithValue("$operation", operation);
        insert.Parameters.AddWithValue("$target", Db(target));
        insert.Parameters.AddWithValue("$requestId", requestId);
        insert.Parameters.AddWithValue("$result", result);
        insert.Parameters.AddWithValue("$detail", detailJson);
        insert.Parameters.AddWithValue("$now", now);
        insert.Parameters.AddWithValue("$previous", previous);
        insert.Parameters.AddWithValue("$hash", hash);
        insert.ExecuteNonQuery();
    }

    private static bool AuditRequestExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actor,
        string requestId,
        string expectedOperation,
        string? expectedTarget)
    {
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT operation,target FROM privileged_audit_events WHERE actor_account=$actor AND request_id=$requestId;";
        read.Parameters.AddWithValue("$actor", actor);
        read.Parameters.AddWithValue("$requestId", requestId);
        using var reader = read.ExecuteReader();
        if (!reader.Read()) return false;
        var operation = reader.GetString(0);
        var target = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (!string.Equals(operation, expectedOperation, StringComparison.Ordinal)
            || !string.Equals(target, expectedTarget, StringComparison.Ordinal))
            throw new OperationsCenterException("request_conflict", "同一 requestId 已用于其他特权操作。");
        return true;
    }

    private static string AuditHash(
        string previous,
        string actor,
        string source,
        string operation,
        string? target,
        string requestId,
        string result,
        string detail,
        long createdAt)
    {
        var material = string.Join('\n', previous, actor, source, operation, target ?? "", requestId, result, detail, createdAt.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static OperationsCaseDetail ReadCaseDetail(
        SqliteConnection connection,
        string caseId,
        SqliteTransaction? transaction = null)
    {
        OperationsCaseSummary summary;
        string description;
        string? external;
        string? appeal;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT c.case_id,c.source,c.category,c.title,c.status,c.priority,
                       c.reporter_account,c.subject_account,c.related_account,c.room_id,c.replay_id,
                       c.assignee,c.disposition,c.created_at_ms,c.first_action_at_ms,c.updated_at_ms,
                       (SELECT COUNT(*) FROM operations_case_evidence e WHERE e.case_id=c.case_id),
                       (SELECT COUNT(*) FROM operations_penalties p WHERE p.case_id=c.case_id
                         AND p.revoked_at_ms IS NULL AND p.expires_at_ms>$now),
                       c.description,c.external_event_id,c.appeal_text
                FROM operations_cases c WHERE c.case_id=$caseId;
                """;
            read.Parameters.AddWithValue("$caseId", caseId);
            read.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            using var reader = read.ExecuteReader();
            if (!reader.Read()) throw new OperationsCenterException("not_found", "Case 不存在。");
            summary = ReadCaseSummary(reader);
            description = reader.GetString(18);
            external = reader.IsDBNull(19) ? null : reader.GetString(19);
            appeal = reader.IsDBNull(20) ? null : reader.GetString(20);
        }

        var evidence = new List<OperationsCaseEvidence>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id,evidence_type,payload_json,created_at_ms,expires_at_ms FROM operations_case_evidence WHERE case_id=$caseId ORDER BY id;";
            read.Parameters.AddWithValue("$caseId", caseId);
            using var reader = read.ExecuteReader();
            while (reader.Read()) evidence.Add(new OperationsCaseEvidence(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        }
        var events = new List<OperationsCaseEvent>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id,event_type,from_status,to_status,actor_account,source,request_id,note,created_at_ms FROM operations_case_events WHERE case_id=$caseId ORDER BY id;";
            read.Parameters.AddWithValue("$caseId", caseId);
            using var reader = read.ExecuteReader();
            while (reader.Read()) events.Add(new OperationsCaseEvent(
                reader.GetInt64(0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3),
                reader.GetString(4), reader.GetString(5), NullableString(reader, 6), reader.GetString(7), reader.GetInt64(8)));
        }
        var penalties = new List<OperationsPenalty>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT penalty_id,case_id,account,kind,reason,operator_account,source,
                       starts_at_ms,expires_at_ms,revoked_at_ms,revoked_by,revoke_reason
                FROM operations_penalties WHERE case_id=$caseId ORDER BY starts_at_ms,penalty_id;
                """;
            read.Parameters.AddWithValue("$caseId", caseId);
            using var reader = read.ExecuteReader();
            while (reader.Read()) penalties.Add(ReadPenalty(reader));
        }
        return new OperationsCaseDetail(summary, description, external, appeal, evidence, events, penalties);
    }

    private static OperationsCaseSummary ReadCaseSummary(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), NullableString(reader, 6), NullableString(reader, 7),
        NullableString(reader, 8), NullableString(reader, 9), NullableString(reader, 10), NullableString(reader, 11),
        NullableString(reader, 12), reader.GetInt64(13), reader.IsDBNull(14) ? null : reader.GetInt64(14),
        reader.GetInt64(15), reader.GetInt32(16), reader.GetInt32(17));

    private static OperationsPenalty ReadPenalty(SqliteConnection connection, string penaltyId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT penalty_id,case_id,account,kind,reason,operator_account,source,
                   starts_at_ms,expires_at_ms,revoked_at_ms,revoked_by,revoke_reason
            FROM operations_penalties WHERE penalty_id=$id;
            """;
        command.Parameters.AddWithValue("$id", penaltyId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new OperationsCenterException("not_found", "处罚记录不存在。");
        return ReadPenalty(reader);
    }

    private static OperationsPenalty ReadPenalty(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt64(7), reader.GetInt64(8),
        reader.IsDBNull(9) ? null : reader.GetInt64(9), NullableString(reader, 10), NullableString(reader, 11));

    private static PrivilegedAuditEntry ReadAudit(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        NullableString(reader, 4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        reader.GetInt64(8), reader.GetString(9), reader.GetString(10));

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _initialized) == 0) Initialize();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(OperationsCenterStore));
    }

    private static string RequireCaseId(string value)
    {
        value = RequiredText(value, 16, 100, "Case ID");
        if (!value.StartsWith("case-", StringComparison.Ordinal) || value.Any(char.IsControl))
            throw new OperationsCenterException("invalid_request", "Case ID 无效。");
        return value;
    }

    private static string RequireAccount(string value) => RequiredText(value, 1, 80, "账号");

    private static string? OptionalAccount(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : RequiredText(value, 1, 80, "账号");
    }

    private static string RequireRequestId(string value)
        => OptionalRequestId(value) ?? throw new OperationsCenterException("invalid_request", "requestId 不能为空。");

    private static string? OptionalRequestId(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length is < 8 or > 120 || value.Any(char.IsControl))
            throw new OperationsCenterException("invalid_request", "requestId 无效。");
        return value;
    }

    private static string RequiredText(string? value, int minimum, int maximum, string label)
    {
        var normalized = (value ?? "").Trim().Normalize(NormalizationForm.FormKC);
        if (normalized.Length < minimum || normalized.Length > maximum || normalized.Any(char.IsControl))
            throw new OperationsCenterException("invalid_request", $"{label}长度或格式无效。");
        return normalized;
    }

    private static string? OptionalText(string? value, int maximum)
    {
        var normalized = value?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum || normalized.Any(char.IsControl))
            throw new OperationsCenterException("invalid_request", "可选文字长度或格式无效。");
        return normalized;
    }

    private static string NormalizeSet(string? value, IReadOnlySet<string> values, string code, string message)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        if (!values.Contains(normalized)) throw new OperationsCenterException(code, message);
        return normalized;
    }

    private static string NormalizeJson(string? value, int maximum)
    {
        value = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        if (Encoding.UTF8.GetByteCount(value) > maximum)
            throw new OperationsCenterException("invalid_request", "JSON 证据或审计详情过大。");
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            throw new OperationsCenterException("invalid_request", "JSON 证据或审计详情格式无效。");
        }
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static object Db(string? value) => value is null ? DBNull.Value : value;

    private static void CopyParameters(SqliteCommand from, SqliteCommand to, params string[] names)
    {
        foreach (var name in names)
            if (from.Parameters.Contains(name)) to.Parameters.AddWithValue(name, from.Parameters[name].Value);
    }
}
