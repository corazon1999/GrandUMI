using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Game.Logging;
using GrandUMI.Training;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public static class CloudReplaySchemas
{
    public const string Document = "grandumi.cloud-replay.v1";
    public const string Runtime = "grandumi.cloud-replay-runtime.v1";
}

public static class CloudReplaySharePolicies
{
    public const string Masked = "masked";
    public const string FinalHands = "final_hands";
    public const string FullTimeline = "full_timeline";

    public static string Normalize(string? value) => value switch
    {
        FinalHands => FinalHands,
        FullTimeline => FullTimeline,
        _ => Masked,
    };
}

public sealed record CloudReplayPlayer(string Account, string DisplayName, bool Record);

public sealed record CloudReplayMatchStart(
    string ReplayId,
    DateTime StartedAtUtc,
    string MatchKind,
    ReplayRuntimeIdentity Runtime,
    CloudReplayPlayer Player0,
    CloudReplayPlayer Player1);

public sealed record CloudReplayCompletion(
    DateTime CompletedAtUtc,
    int? WinnerIndex,
    bool IsDraw,
    string Reason,
    int TurnCount);

public sealed record CloudReplayListQuery(
    string? Opponent,
    string? Outcome,
    string? MatchKind,
    bool BookmarkedOnly,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Offset,
    int Limit);

public sealed record CloudReplayListItem(
    string ReplayId,
    long StartedAt,
    long CompletedAt,
    string MyName,
    string OpponentName,
    string MyLeader,
    string OpponentLeader,
    bool WinnerIsMe,
    bool IsDraw,
    string GameOverReason,
    int TurnCount,
    string MatchKind,
    bool Bookmarked,
    bool Shared,
    string SharePolicy,
    int FeedbackCount,
    long SizeBytes,
    string RuntimeArtifactId);

public sealed record CloudReplayPage(
    IReadOnlyList<CloudReplayListItem> Items,
    int Total,
    long UsedBytes,
    long QuotaBytes,
    int RetentionDays,
    int MaximumReplays);

public sealed record CloudReplayLoadResult(
    string ReplayId,
    bool SharedAccess,
    string SharePolicy,
    JsonElement Document);

public sealed record CloudReplayShareResult(
    string ReplayId,
    bool Shared,
    string SharePolicy,
    string? ShareToken);

public sealed class CloudReplayException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// 账号级云回放。每位参战者拥有独立的玩家视角文件，服务端不会把双方私有视角合并存放；
/// 分享时再按明确策略从所有帧移除隐藏区、Prompt 与请求关联字段。
/// </summary>
public sealed class CloudReplayStore : IDisposable
{
    public const int DefaultRetentionDays = 90;
    public const int DefaultMaximumReplays = 100;
    public const long DefaultQuotaBytes = 256L * 1024 * 1024;
    public const int MaximumSnapshots = 10_000;
    public const long MaximumUncompressedBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly string _root;
    private readonly string _pendingRoot;
    private readonly string _payloadRoot;
    private readonly string _connectionString;
    private readonly Func<string, bool> _runtimeAvailable;
    private readonly AsyncJsonlWriter _writer = new(capacity: 16_384);
    private readonly ConcurrentDictionary<string, CloudReplayCapture> _active = new(StringComparer.Ordinal);
    private readonly int _retentionDays;
    private readonly int _maximumReplays;
    private readonly long _quotaBytes;
    private int _initialized;
    private int _disposed;

    /// <summary>仅供故障演练测试注入；生产代码不得设置。</summary>
    internal Func<string, string, Exception?>? CompletionFailureInjector { get; set; }

    public CloudReplayStore(
        string root,
        Func<string, bool> runtimeAvailable,
        int retentionDays = DefaultRetentionDays,
        int maximumReplays = DefaultMaximumReplays,
        long quotaBytes = DefaultQuotaBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(runtimeAvailable);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumReplays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(quotaBytes, 1024);
        _root = Path.GetFullPath(root);
        _databasePath = Path.Combine(_root, "cloud-replays.db");
        _pendingRoot = Path.Combine(_root, "pending");
        _payloadRoot = Path.Combine(_root, "payload");
        _runtimeAvailable = runtimeAvailable;
        _retentionDays = retentionDays;
        _maximumReplays = maximumReplays;
        _quotaBytes = quotaBytes;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath => _databasePath;
    public string Root => _root;

    public void Initialize()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _initialized) != 0) return;
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_pendingRoot);
            Directory.CreateDirectory(_payloadRoot);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = FULL;
                PRAGMA busy_timeout = 5000;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS cloud_replays (
                    replay_id              TEXT NOT NULL,
                    owner_account          TEXT NOT NULL COLLATE NOCASE,
                    participant_index      INTEGER NOT NULL,
                    opponent_account       TEXT NOT NULL COLLATE NOCASE,
                    started_at_ms          INTEGER NOT NULL,
                    completed_at_ms        INTEGER NOT NULL,
                    my_name                TEXT NOT NULL,
                    opponent_name          TEXT NOT NULL,
                    my_leader              TEXT NOT NULL,
                    opponent_leader        TEXT NOT NULL,
                    winner_is_me           INTEGER NOT NULL,
                    is_draw                INTEGER NOT NULL,
                    game_over_reason       TEXT NOT NULL,
                    turn_count             INTEGER NOT NULL,
                    match_kind             TEXT NOT NULL,
                    document_schema        TEXT NOT NULL,
                    runtime_artifact_id     TEXT NOT NULL,
                    runtime_manifest_hash   TEXT NOT NULL,
                    payload_path           TEXT NOT NULL,
                    size_bytes             INTEGER NOT NULL,
                    bookmarked             INTEGER NOT NULL DEFAULT 0,
                    share_token_hash        TEXT,
                    share_policy            TEXT NOT NULL DEFAULT 'masked',
                    shared_at_ms            INTEGER,
                    PRIMARY KEY (replay_id, owner_account)
                );

                CREATE INDEX IF NOT EXISTS ix_cloud_replays_owner_time
                    ON cloud_replays(owner_account, completed_at_ms DESC);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_cloud_replays_share_token
                    ON cloud_replays(share_token_hash) WHERE share_token_hash IS NOT NULL;

                CREATE TABLE IF NOT EXISTS cloud_replay_feedback (
                    replay_id       TEXT NOT NULL,
                    owner_account   TEXT NOT NULL COLLATE NOCASE,
                    feedback_id     TEXT NOT NULL,
                    linked_at_ms    INTEGER NOT NULL,
                    PRIMARY KEY (replay_id, owner_account, feedback_id),
                    FOREIGN KEY (replay_id, owner_account)
                        REFERENCES cloud_replays(replay_id, owner_account) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS cloud_replay_mutations (
                    owner_account  TEXT NOT NULL COLLATE NOCASE,
                    request_id     TEXT NOT NULL,
                    operation      TEXT NOT NULL,
                    response_json  TEXT NOT NULL,
                    created_at_ms  INTEGER NOT NULL,
                    PRIMARY KEY (owner_account, request_id)
                );
                """;
            command.ExecuteNonQuery();
            CleanupOrphans(connection);
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM cloud_replay_mutations WHERE created_at_ms < $cutoff;";
            cleanup.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds());
            cleanup.ExecuteNonQuery();
            Volatile.Write(ref _initialized, 1);
        }
    }

    public CloudReplayCapture? BeginMatch(CloudReplayMatchStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        EnsureInitialized();
        if (!IsSafeReplayId(start.ReplayId))
            throw new ArgumentException("云回放 ID 格式无效。", nameof(start));
        if (!start.Player0.Record && !start.Player1.Record) return null;
        ValidatePlayer(start.Player0);
        ValidatePlayer(start.Player1);

        var keys = new string?[2];
        var paths = new string?[2];
        var metadataPath = PendingMetadataPath(start.ReplayId);
        if (File.Exists(metadataPath))
            throw new IOException($"云回放恢复元数据已存在：{start.ReplayId}");
        for (var index = 0; index < 2; index++)
        {
            var player = index == 0 ? start.Player0 : start.Player1;
            if (!player.Record) continue;
            keys[index] = $"cloud:{start.ReplayId}:{index}";
            paths[index] = ResolveInside(_pendingRoot, $"{start.ReplayId}.p{index}.jsonl");
            if (File.Exists(paths[index]))
                throw new IOException($"云回放暂存文件已存在：{start.ReplayId}/P{index}");
        }

        var capture = new CloudReplayCapture(this, start, keys, paths, metadataPath);
        if (!_active.TryAdd(start.ReplayId, capture))
            throw new InvalidOperationException($"云回放已经开始：{start.ReplayId}");
        try
        {
            WriteMetadataAtomic(metadataPath, start);
            for (var index = 0; index < 2; index++)
                if (keys[index] is not null && paths[index] is not null)
                    _writer.OpenRequired(keys[index]!, paths[index]!, append: false);
            return capture;
        }
        catch
        {
            _active.TryRemove(start.ReplayId, out _);
            // 第二个参与者文件打开失败时，第一个文件可能已经由后台写入器持有。
            // 必须有序关闭并删除所有本次捕获的暂存文件，避免句柄泄漏和同 ID 永久无法重试。
            foreach (var key in keys)
            {
                if (key is null) continue;
                try { _writer.Close(key); } catch { }
            }
            foreach (var path in paths)
                if (path is not null) TryDeleteFile(path);
            TryDeleteFile(metadataPath);
            throw;
        }
    }

    /// <summary>
    /// 进程恢复时重新打开尚未发布的玩家视角磁带。元数据与磁带都留在 pending 下，
    /// 因而进行中对局可续录，已经写入终局 WAL 的对局也可继续同一次发布。
    /// </summary>
    internal CloudReplayCapture? ResumeMatch(string replayId)
    {
        EnsureInitialized();
        if (!IsSafeReplayId(replayId)) return null;
        if (_active.TryGetValue(replayId, out var active)) return active;
        var metadataPath = PendingMetadataPath(replayId);
        if (!File.Exists(metadataPath)) return null;

        var start = JsonSerializer.Deserialize<CloudReplayMatchStart>(
                        File.ReadAllBytes(metadataPath), JsonOptions)
                    ?? throw new InvalidDataException($"云回放 {replayId} 恢复元数据为空");
        if (!string.Equals(start.ReplayId, replayId, StringComparison.Ordinal))
            throw new InvalidDataException($"云回放 {replayId} 恢复元数据 ID 不一致");
        var keys = new string?[2];
        var paths = new string?[2];
        var initialFrameCounts = new int[2];
        var initialLastTicks = new[] { -1, -1 };
        var initialTerminalFrames = new bool[2];
        for (var index = 0; index < 2; index++)
        {
            var player = index == 0 ? start.Player0 : start.Player1;
            if (!player.Record) continue;
            keys[index] = $"cloud:{replayId}:{index}";
            paths[index] = ResolveInside(_pendingRoot, $"{replayId}.p{index}.jsonl");
            if (!File.Exists(paths[index]))
                throw new InvalidDataException($"云回放 {replayId}/P{index} 的恢复磁带缺失");
            var pending = InspectPendingTape(paths[index]!);
            initialFrameCounts[index] = pending.FrameCount;
            initialLastTicks[index] = pending.LastTick;
            initialTerminalFrames[index] = pending.HasTerminalFrame;
        }

        var capture = new CloudReplayCapture(
            this,
            start,
            keys,
            paths,
            metadataPath,
            initialFrameCounts,
            initialLastTicks,
            initialTerminalFrames);
        if (!_active.TryAdd(replayId, capture)) return _active[replayId];
        try
        {
            for (var index = 0; index < 2; index++)
                if (keys[index] is not null && paths[index] is not null)
                    _writer.OpenRequired(keys[index]!, paths[index]!, append: true);
            return capture;
        }
        catch
        {
            _active.TryRemove(new KeyValuePair<string, CloudReplayCapture>(replayId, capture));
            foreach (var key in keys)
            {
                if (key is null) continue;
                try { _writer.Close(key); } catch { }
            }
            throw;
        }
    }

    public CloudReplayPage List(string account, CloudReplayListQuery query)
    {
        account = RequireAccount(account);
        ArgumentNullException.ThrowIfNull(query);
        EnsureInitialized();
        var limit = Math.Clamp(query.Limit, 1, 100);
        var offset = Math.Clamp(query.Offset, 0, 10_000);
        lock (_gate)
        {
            using var connection = OpenConnection();
            var where = new List<string> { "r.owner_account = $owner" };
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$owner", account);
            if (!string.IsNullOrWhiteSpace(query.Opponent))
            {
                where.Add("r.opponent_name LIKE $opponent ESCAPE '\\'");
                command.Parameters.AddWithValue("$opponent", $"%{EscapeLike(query.Opponent.Trim())}%");
            }
            if (query.BookmarkedOnly) where.Add("r.bookmarked = 1");
            if (!string.IsNullOrWhiteSpace(query.MatchKind))
            {
                where.Add("r.match_kind = $matchKind");
                command.Parameters.AddWithValue("$matchKind", query.MatchKind.Trim());
            }
            switch (query.Outcome)
            {
                case "win": where.Add("r.is_draw = 0 AND r.winner_is_me = 1"); break;
                case "loss": where.Add("r.is_draw = 0 AND r.winner_is_me = 0"); break;
                case "draw": where.Add("r.is_draw = 1"); break;
            }
            if (query.FromUtc is { } from)
            {
                where.Add("r.started_at_ms >= $from");
                command.Parameters.AddWithValue("$from", new DateTimeOffset(from.ToUniversalTime()).ToUnixTimeMilliseconds());
            }
            if (query.ToUtc is { } to)
            {
                where.Add("r.started_at_ms <= $to");
                command.Parameters.AddWithValue("$to", new DateTimeOffset(to.ToUniversalTime()).ToUnixTimeMilliseconds());
            }
            var predicate = string.Join(" AND ", where);
            command.CommandText = $"""
                SELECT r.replay_id, r.started_at_ms, r.completed_at_ms,
                       r.my_name, r.opponent_name, r.my_leader, r.opponent_leader,
                       r.winner_is_me, r.is_draw, r.game_over_reason, r.turn_count,
                       r.match_kind, r.bookmarked, r.share_token_hash IS NOT NULL,
                       r.share_policy,
                       (SELECT COUNT(*) FROM cloud_replay_feedback f
                         WHERE f.replay_id = r.replay_id AND f.owner_account = r.owner_account),
                       r.size_bytes, r.runtime_artifact_id
                FROM cloud_replays r
                WHERE {predicate}
                ORDER BY r.bookmarked DESC, r.started_at_ms DESC, r.replay_id DESC
                LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            var items = new List<CloudReplayListItem>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    items.Add(new CloudReplayListItem(
                        reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                        reader.GetInt64(7) != 0, reader.GetInt64(8) != 0, reader.GetString(9),
                        reader.GetInt32(10), reader.GetString(11), reader.GetInt64(12) != 0,
                        reader.GetInt64(13) != 0, reader.GetString(14), reader.GetInt32(15),
                        reader.GetInt64(16), reader.GetString(17)));
                }
            }

            using var aggregate = connection.CreateCommand();
            aggregate.CommandText = $"""
                SELECT COUNT(*), COALESCE(SUM(size_bytes), 0)
                FROM cloud_replays r WHERE {predicate};
                """;
            foreach (SqliteParameter parameter in command.Parameters)
            {
                if (parameter.ParameterName is "$limit" or "$offset") continue;
                aggregate.Parameters.AddWithValue(parameter.ParameterName, parameter.Value);
            }
            using var totals = aggregate.ExecuteReader();
            totals.Read();
            return new CloudReplayPage(
                items, totals.GetInt32(0), totals.GetInt64(1), _quotaBytes, _retentionDays, _maximumReplays);
        }
    }

    public CloudReplayLoadResult Load(string account, string replayId, string? shareToken = null)
    {
        account = RequireAccount(account);
        replayId = RequireReplayId(replayId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            var sharedAccess = !string.IsNullOrWhiteSpace(shareToken);
            command.CommandText = sharedAccess
                ? """
                    SELECT payload_path, runtime_artifact_id, share_policy, owner_account
                    FROM cloud_replays
                    WHERE replay_id = $replayId AND share_token_hash = $tokenHash;
                    """
                : """
                    SELECT payload_path, runtime_artifact_id, share_policy, owner_account
                    FROM cloud_replays
                    WHERE replay_id = $replayId AND owner_account = $owner;
                    """;
            command.Parameters.AddWithValue("$replayId", replayId);
            command.Parameters.AddWithValue("$owner", account);
            command.Parameters.AddWithValue(
                "$tokenHash",
                sharedAccess ? HashToken(shareToken!.Trim()) : DBNull.Value);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new CloudReplayException("not_found", "未找到回放，或当前账号没有访问权限。");
            var payloadPath = ResolveInside(_root, reader.GetString(0));
            var runtimeArtifactId = reader.GetString(1);
            var sharePolicy = CloudReplaySharePolicies.Normalize(reader.GetString(2));
            if (!_runtimeAvailable(runtimeArtifactId))
                throw new CloudReplayException(
                    "runtime_missing",
                    $"该历史回放所需运行时 {runtimeArtifactId} 尚未归档，暂时无法打开。");
            var document = ReadDocument(payloadPath);
            if (sharedAccess)
                document = ApplySharePolicy(document, sharePolicy);
            return new CloudReplayLoadResult(replayId, sharedAccess, sharePolicy, document);
        }
    }

    public bool SetBookmark(string account, string replayId, bool bookmarked, string requestId)
    {
        account = RequireAccount(account);
        replayId = RequireReplayId(replayId);
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            if (TryReadMutation<bool>(connection, account, requestId, "bookmark", out var replayed))
                return replayed;
            using var tx = connection.BeginTransaction();
            using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE cloud_replays SET bookmarked = $bookmarked
                WHERE replay_id = $replayId AND owner_account = $owner;
                """;
            update.Parameters.AddWithValue("$bookmarked", bookmarked ? 1 : 0);
            update.Parameters.AddWithValue("$replayId", replayId);
            update.Parameters.AddWithValue("$owner", account);
            if (update.ExecuteNonQuery() != 1)
                throw new CloudReplayException("not_found", "未找到回放，或当前账号没有修改权限。");
            WriteMutation(connection, tx, account, requestId, "bookmark", bookmarked);
            tx.Commit();
            return bookmarked;
        }
    }

    public CloudReplayShareResult SetShare(
        string account,
        string replayId,
        bool enabled,
        string? policy,
        string requestId)
    {
        account = RequireAccount(account);
        replayId = RequireReplayId(replayId);
        requestId = RequireRequestId(requestId);
        var normalizedPolicy = CloudReplaySharePolicies.Normalize(policy);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            if (TryReadMutation<CloudReplayShareResult>(connection, account, requestId, "share", out var replayed))
                return replayed;
            var token = enabled ? CreateShareToken() : null;
            var result = new CloudReplayShareResult(replayId, enabled, normalizedPolicy, token);
            using var tx = connection.BeginTransaction();
            using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE cloud_replays
                SET share_token_hash = $tokenHash,
                    share_policy = $policy,
                    shared_at_ms = $sharedAt
                WHERE replay_id = $replayId AND owner_account = $owner;
                """;
            update.Parameters.AddWithValue("$tokenHash", token is null ? DBNull.Value : HashToken(token));
            update.Parameters.AddWithValue("$policy", normalizedPolicy);
            update.Parameters.AddWithValue(
                "$sharedAt", enabled ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : DBNull.Value);
            update.Parameters.AddWithValue("$replayId", replayId);
            update.Parameters.AddWithValue("$owner", account);
            if (update.ExecuteNonQuery() != 1)
                throw new CloudReplayException("not_found", "未找到回放，或当前账号没有分享权限。");
            WriteMutation(connection, tx, account, requestId, "share", result);
            tx.Commit();
            return result;
        }
    }

    public bool Delete(string account, string replayId, string requestId)
    {
        account = RequireAccount(account);
        replayId = RequireReplayId(replayId);
        requestId = RequireRequestId(requestId);
        EnsureInitialized();
        lock (_gate)
        {
            using var connection = OpenConnection();
            if (TryReadMutation<bool>(connection, account, requestId, "delete", out var replayed))
                return replayed;
            using var find = connection.CreateCommand();
            find.CommandText = "SELECT payload_path FROM cloud_replays WHERE replay_id = $replayId AND owner_account = $owner;";
            find.Parameters.AddWithValue("$replayId", replayId);
            find.Parameters.AddWithValue("$owner", account);
            var relativePath = find.ExecuteScalar() as string;
            if (relativePath is null)
                throw new CloudReplayException("not_found", "未找到回放，或当前账号没有删除权限。");
            using var tx = connection.BeginTransaction();
            using var delete = connection.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM cloud_replays WHERE replay_id = $replayId AND owner_account = $owner;";
            delete.Parameters.AddWithValue("$replayId", replayId);
            delete.Parameters.AddWithValue("$owner", account);
            delete.ExecuteNonQuery();
            WriteMutation(connection, tx, account, requestId, "delete", true);
            tx.Commit();
            DeletePayload(relativePath);
            return true;
        }
    }

    public bool AssociateFeedback(string account, string replayId, string feedbackId)
    {
        account = RequireAccount(account);
        replayId = RequireReplayId(replayId);
        if (string.IsNullOrWhiteSpace(feedbackId) || feedbackId.Length > 120)
            throw new ArgumentException("反馈 ID 无效。", nameof(feedbackId));
        EnsureInitialized();
        if (_active.TryGetValue(replayId, out var capture))
            return capture.TryAddFeedback(account, feedbackId.Trim());
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO cloud_replay_feedback (
                    replay_id, owner_account, feedback_id, linked_at_ms)
                SELECT replay_id, owner_account, $feedbackId, $linkedAt
                FROM cloud_replays
                WHERE replay_id = $replayId AND owner_account = $owner;
                """;
            command.Parameters.AddWithValue("$feedbackId", feedbackId.Trim());
            command.Parameters.AddWithValue("$linkedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$replayId", replayId);
            command.Parameters.AddWithValue("$owner", account);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var capture in _active.Values) capture.MarkAborted();
        _active.Clear();
        _writer.Shutdown();
    }

    internal void Append(CloudReplayCapture capture, int playerIndex, object payload)
    {
        if (capture.IsAborted || playerIndex is not (0 or 1)) return;
        var key = capture.Keys[playerIndex];
        if (key is null) return;
        JsonElement snapshot;
        try
        {
            snapshot = JsonSerializer.SerializeToElement(payload, JsonOptions);
            if (!snapshot.TryGetProperty("proto", out var proto)
                || !string.Equals(proto.GetString(), "MsgGameState", StringComparison.Ordinal))
                return;
            if (!snapshot.TryGetProperty("viewerKind", out var viewer)
                || !string.Equals(viewer.GetString(), "player", StringComparison.Ordinal))
                throw new InvalidDataException("云回放只允许写入玩家视角快照。");
            var tick = snapshot.GetProperty("tick").GetInt32();
            if (capture.RegisterFrame(playerIndex, tick, TryBoolean(snapshot, "isGameOver")) > MaximumSnapshots)
                throw new InvalidDataException($"云回放超过 {MaximumSnapshots} 帧上限。");
            _writer.AppendRequired(key, snapshot);
        }
        catch (Exception ex)
        {
            capture.MarkFailed(ex.Message);
            Console.Error.WriteLine($"[云回放] {capture.Start.ReplayId}/P{playerIndex} 捕获失败：{ex.Message}");
        }
    }

    internal async Task CompleteAsync(CloudReplayCapture capture, CloudReplayCompletion completion)
    {
        if (!_active.TryGetValue(capture.Start.ReplayId, out var active)
            || !ReferenceEquals(active, capture)) return;
        var finalPayloads = new List<string>();
        var databaseCommitted = false;
        try
        {
            await CloseCaptureFiles(capture);
            if (capture.FailureReason is { } failure)
                throw new InvalidDataException(failure);
            if (IsCompletionPublished(capture.Start))
            {
                CompletePendingCleanup(capture);
                return;
            }
            if (CompletionFailureInjector?.Invoke(capture.Start.ReplayId, "before_publish") is { } beforePublish)
                throw beforePublish;

            var rows = new List<CompletedView>();
            for (var index = 0; index < 2; index++)
            {
                var player = index == 0 ? capture.Start.Player0 : capture.Start.Player1;
                if (!player.Record) continue;
                var path = capture.Paths[index]
                    ?? throw new InvalidOperationException("云回放暂存路径缺失。");
                var snapshots = ReadSnapshots(path);
                var document = BuildDocument(capture.Start, completion, index, snapshots);
                var payloadPath = PayloadPath(player.Account, capture.Start.ReplayId);
                WriteDocumentAtomic(payloadPath, document);
                finalPayloads.Add(payloadPath);
                rows.Add(BuildCompletedView(capture.Start, completion, index, snapshots, payloadPath));
            }
            if (CompletionFailureInjector?.Invoke(capture.Start.ReplayId, "after_payloads") is { } afterPayloads)
                throw afterPayloads;

            lock (_gate)
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction();
                foreach (var row in rows) InsertCompletedView(connection, tx, row);
                foreach (var feedback in capture.FeedbackLinks)
                    InsertFeedback(connection, tx, capture.Start.ReplayId, feedback.Account, feedback.FeedbackId);
                tx.Commit();
                databaseCommitted = true;
                foreach (var account in rows.Select(row => row.OwnerAccount).Distinct(StringComparer.OrdinalIgnoreCase))
                    EnforceRetentionAndQuota(connection, account);
            }
            CompletePendingCleanup(capture);
        }
        catch (Exception ex)
        {
            if (databaseCommitted)
            {
                // 数据库事务是发布提交点；其后的配额维护失败不能把已引用载荷删掉或要求重复发布。
                Console.Error.WriteLine($"[云回放] {capture.Start.ReplayId} 已发布，后置维护失败：{ex.Message}");
                CompletePendingCleanup(capture);
                return;
            }
            foreach (var path in finalPayloads) TryDeleteFile(path);
            Console.Error.WriteLine($"[云回放] {capture.Start.ReplayId} 完成失败，保留恢复磁带等待重试：{ex.Message}");
            throw;
        }
    }

    internal async Task AbortAsync(CloudReplayCapture capture)
    {
        capture.MarkAborted();
        _active.TryRemove(new KeyValuePair<string, CloudReplayCapture>(capture.Start.ReplayId, capture));
        await CloseCaptureFiles(capture);
        foreach (var path in capture.Paths)
            if (path is not null) TryDeleteFile(path);
        TryDeleteFile(capture.MetadataPath);
    }

    private async Task CloseCaptureFiles(CloudReplayCapture capture)
    {
        var closes = capture.Keys.Where(key => key is not null)
            .Select(key => _writer.CloseDeferred(key!));
        await Task.WhenAll(closes);
    }

    private bool IsCompletionPublished(CloudReplayMatchStart start)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT owner_account FROM cloud_replays WHERE replay_id = $replayId;";
            command.Parameters.AddWithValue("$replayId", start.ReplayId);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(0));
            var expected = new[] { start.Player0, start.Player1 }
                .Where(player => player.Record)
                .Select(player => player.Account)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existing.Count == 0) return false;
            if (!existing.SetEquals(expected))
                throw new InvalidDataException($"云回放 {start.ReplayId} 已发布参与者集合不完整或冲突");
            return true;
        }
    }

    private void CompletePendingCleanup(CloudReplayCapture capture)
    {
        _active.TryRemove(new KeyValuePair<string, CloudReplayCapture>(capture.Start.ReplayId, capture));
        foreach (var path in capture.Paths)
            if (path is not null) TryDeleteFile(path);
        TryDeleteFile(capture.MetadataPath);
    }

    private JsonElement BuildDocument(
        CloudReplayMatchStart start,
        CloudReplayCompletion completion,
        int playerIndex,
        JsonElement[] snapshots)
    {
        var first = snapshots[0];
        var last = snapshots[^1];
        var my = first.GetProperty("my");
        var opponent = first.GetProperty("opponent");
        var runtime = start.Runtime;
        return JsonSerializer.SerializeToElement(new
        {
            format = "grandumi-replay",
            version = 1,
            exportedAt = completion.CompletedAtUtc.ToUniversalTime().ToString("O"),
            cloudSchema = CloudReplaySchemas.Document,
            runtime = new
            {
                schema = CloudReplaySchemas.Runtime,
                engineArtifactId = runtime.EngineArtifactId,
                runtimeManifestHash = runtime.ManifestHash,
                engineCommit = runtime.EngineCommit,
                rulesetId = runtime.RulesVersion,
                rulesetManifestHash = runtime.RulesetManifestHash,
                cardDbContentHash = runtime.CardDbContentHash,
            },
            meta = new
            {
                id = start.ReplayId,
                startedAt = new DateTimeOffset(start.StartedAtUtc.ToUniversalTime()).ToUnixTimeMilliseconds(),
                myName = my.GetProperty("name").GetString() ?? "",
                opponentName = opponent.GetProperty("name").GetString() ?? "",
                myLeader = my.GetProperty("leaderNumber").GetString() ?? "",
                opponentLeader = opponent.GetProperty("leaderNumber").GetString() ?? "",
                winnerIsMe = !completion.IsDraw && completion.WinnerIndex == playerIndex,
                isDraw = completion.IsDraw,
                diceWinnerIsMe = TryBoolean(first, "diceWinnerIsMe"),
                isFirstPlayer = TryBoolean(first, "isFirstPlayer"),
                gameOverReason = completion.Reason,
                turnCount = completion.TurnCount,
                snapshotCount = snapshots.Length,
            },
            snapshots,
        }, JsonOptions);
    }

    private CompletedView BuildCompletedView(
        CloudReplayMatchStart start,
        CloudReplayCompletion completion,
        int playerIndex,
        JsonElement[] snapshots,
        string payloadPath)
    {
        var player = playerIndex == 0 ? start.Player0 : start.Player1;
        var opponentPlayer = playerIndex == 0 ? start.Player1 : start.Player0;
        var first = snapshots[0];
        var my = first.GetProperty("my");
        var opponent = first.GetProperty("opponent");
        return new CompletedView(
            start.ReplayId,
            player.Account,
            playerIndex,
            opponentPlayer.Account,
            new DateTimeOffset(start.StartedAtUtc.ToUniversalTime()).ToUnixTimeMilliseconds(),
            new DateTimeOffset(completion.CompletedAtUtc.ToUniversalTime()).ToUnixTimeMilliseconds(),
            my.GetProperty("name").GetString() ?? player.DisplayName,
            opponent.GetProperty("name").GetString() ?? opponentPlayer.DisplayName,
            my.GetProperty("leaderNumber").GetString() ?? "",
            opponent.GetProperty("leaderNumber").GetString() ?? "",
            !completion.IsDraw && completion.WinnerIndex == playerIndex,
            completion.IsDraw,
            completion.Reason,
            completion.TurnCount,
            start.MatchKind,
            start.Runtime.EngineArtifactId,
            start.Runtime.ManifestHash,
            Path.GetRelativePath(_root, payloadPath).Replace('\\', '/'),
            new FileInfo(payloadPath).Length);
    }

    private static JsonElement[] ReadSnapshots(string path)
    {
        var snapshots = new List<JsonElement>();
        long bytes = 0;
        var previousTick = -1;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            bytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes > MaximumUncompressedBytes)
                throw new InvalidDataException("云回放未压缩内容超过 64 MiB 上限。");
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var snapshot = document.RootElement;
            if (snapshot.GetProperty("proto").GetString() != "MsgGameState")
                throw new InvalidDataException("云回放包含非状态快照。");
            if (snapshot.GetProperty("viewerKind").GetString() != "player")
                throw new InvalidDataException("云回放包含非玩家视角。");
            var tick = snapshot.GetProperty("tick").GetInt32();
            if (tick <= previousTick)
                throw new InvalidDataException("云回放 Tick 必须严格递增。");
            previousTick = tick;
            if (!TryBoolean(snapshot, "isGameOver"))
            {
                var opponent = snapshot.GetProperty("opponent");
                if (opponent.GetProperty("handCardIds").GetArrayLength() != 0
                    || opponent.GetProperty("handCardNumbers").GetArrayLength() != 0)
                    throw new InvalidDataException("云回放在终局前泄露了对手手牌，拒绝发布。");
            }
            snapshots.Add(snapshot.Clone());
            if (snapshots.Count > MaximumSnapshots)
                throw new InvalidDataException($"云回放超过 {MaximumSnapshots} 帧上限。");
        }
        if (snapshots.Count == 0) throw new InvalidDataException("云回放没有快照。");
        if (!TryBoolean(snapshots[^1], "isGameOver"))
            throw new InvalidDataException("云回放最后一帧不是终局状态。");
        return snapshots.ToArray();
    }

    /// <summary>
    /// 恢复时先校验暂存磁带并取得最后一帧边界。允许零帧和非终局尾帧，因为进程可能在
    /// 开局广播或终局广播的任意两次玩家下发之间退出；真正发布仍由 ReadSnapshots 做完整校验。
    /// </summary>
    private static PendingTapeState InspectPendingTape(string path)
    {
        var frameCount = 0;
        var lastTick = -1;
        var hasTerminalFrame = false;
        long bytes = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            bytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes > MaximumUncompressedBytes)
                throw new InvalidDataException("云回放恢复磁带超过 64 MiB 上限。");
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var snapshot = document.RootElement;
            if (snapshot.GetProperty("proto").GetString() != "MsgGameState"
                || snapshot.GetProperty("viewerKind").GetString() != "player")
                throw new InvalidDataException("云回放恢复磁带包含非玩家状态快照。");
            var tick = snapshot.GetProperty("tick").GetInt32();
            if (tick <= lastTick)
                throw new InvalidDataException("云回放恢复磁带 Tick 必须严格递增。");
            if (hasTerminalFrame)
                throw new InvalidDataException("云回放恢复磁带在终局帧之后仍有状态。");
            lastTick = tick;
            hasTerminalFrame = TryBoolean(snapshot, "isGameOver");
            frameCount++;
            if (frameCount > MaximumSnapshots)
                throw new InvalidDataException($"云回放恢复磁带超过 {MaximumSnapshots} 帧上限。");
        }
        return new PendingTapeState(frameCount, lastTick, hasTerminalFrame);
    }

    private static JsonElement ApplySharePolicy(JsonElement document, string policy)
    {
        var root = JsonNode.Parse(document.GetRawText())?.AsObject()
            ?? throw new InvalidDataException("云回放文档损坏。");
        var snapshots = root["snapshots"]?.AsArray()
            ?? throw new InvalidDataException("云回放缺少快照。");
        foreach (var node in snapshots)
        {
            var snapshot = node?.AsObject() ?? throw new InvalidDataException("云回放快照损坏。");
            var terminal = snapshot["isGameOver"]?.GetValue<bool>() == true;
            if (policy != CloudReplaySharePolicies.FullTimeline
                && (policy == CloudReplaySharePolicies.Masked || !terminal))
            {
                ScrubPlayerHand(snapshot["my"]?.AsObject());
                ScrubPlayerHand(snapshot["opponent"]?.AsObject());
            }
            if (policy != CloudReplaySharePolicies.FullTimeline)
                snapshot["replayHands"] = null;
            snapshot["pendingPrompt"] = null;
            snapshot["requestId"] = null;
            snapshot["actionPayload"] = "";
        }
        return JsonSerializer.SerializeToElement(root, JsonOptions);
    }

    private static void ScrubPlayerHand(JsonObject? player)
    {
        if (player is null) return;
        player["handCardIds"] = new JsonArray();
        player["handCardNumbers"] = new JsonArray();
        player["handCardCosts"] = new JsonArray();
        player["handCardCounters"] = new JsonArray();
    }

    private void WriteDocumentAtomic(string path, JsonElement document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true))
                JsonSerializer.Serialize(gzip, document, JsonOptions);
            // 同一终局在“数据库提交后进程退出”的极窄窗口内可能被恢复重试；
            // 内容由同一终局 WAL 决定，允许原子替换同一路径而不生成第二份回放。
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private static JsonElement ReadDocument(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var limited = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > MaximumUncompressedBytes)
                throw new InvalidDataException("云回放解压后超过 64 MiB 上限。");
            limited.Write(buffer, 0, read);
        }
        limited.Position = 0;
        using var document = JsonDocument.Parse(limited);
        var root = document.RootElement;
        if (root.GetProperty("cloudSchema").GetString() != CloudReplaySchemas.Document)
            throw new InvalidDataException("云回放文档版本不受支持。");
        return root.Clone();
    }

    private void InsertCompletedView(SqliteConnection connection, SqliteTransaction tx, CompletedView row)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO cloud_replays (
                replay_id, owner_account, participant_index, opponent_account,
                started_at_ms, completed_at_ms, my_name, opponent_name,
                my_leader, opponent_leader, winner_is_me, is_draw,
                game_over_reason, turn_count, match_kind, document_schema,
                runtime_artifact_id, runtime_manifest_hash, payload_path, size_bytes,
                bookmarked, share_policy
            ) VALUES (
                $replayId, $owner, $index, $opponentAccount,
                $startedAt, $completedAt, $myName, $opponentName,
                $myLeader, $opponentLeader, $winnerIsMe, $isDraw,
                $reason, $turnCount, $matchKind, $schema,
                $runtimeArtifactId, $runtimeManifestHash, $payloadPath, $sizeBytes,
                0, 'masked'
            );
            """;
        command.Parameters.AddWithValue("$replayId", row.ReplayId);
        command.Parameters.AddWithValue("$owner", row.OwnerAccount);
        command.Parameters.AddWithValue("$index", row.ParticipantIndex);
        command.Parameters.AddWithValue("$opponentAccount", row.OpponentAccount);
        command.Parameters.AddWithValue("$startedAt", row.StartedAt);
        command.Parameters.AddWithValue("$completedAt", row.CompletedAt);
        command.Parameters.AddWithValue("$myName", row.MyName);
        command.Parameters.AddWithValue("$opponentName", row.OpponentName);
        command.Parameters.AddWithValue("$myLeader", row.MyLeader);
        command.Parameters.AddWithValue("$opponentLeader", row.OpponentLeader);
        command.Parameters.AddWithValue("$winnerIsMe", row.WinnerIsMe ? 1 : 0);
        command.Parameters.AddWithValue("$isDraw", row.IsDraw ? 1 : 0);
        command.Parameters.AddWithValue("$reason", row.Reason);
        command.Parameters.AddWithValue("$turnCount", row.TurnCount);
        command.Parameters.AddWithValue("$matchKind", row.MatchKind);
        command.Parameters.AddWithValue("$schema", CloudReplaySchemas.Document);
        command.Parameters.AddWithValue("$runtimeArtifactId", row.RuntimeArtifactId);
        command.Parameters.AddWithValue("$runtimeManifestHash", row.RuntimeManifestHash);
        command.Parameters.AddWithValue("$payloadPath", row.PayloadPath);
        command.Parameters.AddWithValue("$sizeBytes", row.SizeBytes);
        command.ExecuteNonQuery();
    }

    private static void InsertFeedback(
        SqliteConnection connection,
        SqliteTransaction tx,
        string replayId,
        string account,
        string feedbackId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT OR IGNORE INTO cloud_replay_feedback (
                replay_id, owner_account, feedback_id, linked_at_ms)
            VALUES ($replayId, $owner, $feedbackId, $linkedAt);
            """;
        command.Parameters.AddWithValue("$replayId", replayId);
        command.Parameters.AddWithValue("$owner", account);
        command.Parameters.AddWithValue("$feedbackId", feedbackId);
        command.Parameters.AddWithValue("$linkedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private void EnforceRetentionAndQuota(SqliteConnection connection, string account)
    {
        var rows = new List<(string ReplayId, string Path, long Size, bool Bookmarked, long CompletedAt)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT replay_id, payload_path, size_bytes, bookmarked, completed_at_ms
                FROM cloud_replays WHERE owner_account = $owner
                ORDER BY completed_at_ms DESC, replay_id DESC;
                """;
            command.Parameters.AddWithValue("$owner", account);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3) != 0, reader.GetInt64(4)));
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays).ToUnixTimeMilliseconds();
        var delete = new HashSet<string>(StringComparer.Ordinal);
        var unbookmarkedSeen = 0;
        foreach (var row in rows)
        {
            if (row.Bookmarked) continue;
            unbookmarkedSeen++;
            if (row.CompletedAt < cutoff || unbookmarkedSeen > _maximumReplays)
                delete.Add(row.ReplayId);
        }
        var used = rows.Where(row => !delete.Contains(row.ReplayId)).Sum(row => row.Size);
        foreach (var row in rows.Where(row => !delete.Contains(row.ReplayId))
                     .OrderBy(row => row.Bookmarked)
                     .ThenBy(row => row.CompletedAt))
        {
            if (used <= _quotaBytes) break;
            delete.Add(row.ReplayId);
            used -= row.Size;
        }
        foreach (var row in rows.Where(row => delete.Contains(row.ReplayId)))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cloud_replays WHERE replay_id = $replayId AND owner_account = $owner;";
            command.Parameters.AddWithValue("$replayId", row.ReplayId);
            command.Parameters.AddWithValue("$owner", account);
            command.ExecuteNonQuery();
            DeletePayload(row.Path);
        }
    }

    private void CleanupOrphans(SqliteConnection connection)
    {
        var validPendingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metadataPath in Directory.EnumerateFiles(
                     _pendingRoot, "*.meta.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var start = JsonSerializer.Deserialize<CloudReplayMatchStart>(
                                File.ReadAllBytes(metadataPath), JsonOptions)
                            ?? throw new InvalidDataException("恢复元数据为空");
                if (!IsSafeReplayId(start.ReplayId)
                    || !string.Equals(metadataPath, PendingMetadataPath(start.ReplayId), PathComparison()))
                    throw new InvalidDataException("恢复元数据路径与回放 ID 不一致");
                var expectedPaths = new[] { start.Player0, start.Player1 }
                    .Select((player, index) => player.Record
                        ? ResolveInside(_pendingRoot, $"{start.ReplayId}.p{index}.jsonl")
                        : null)
                    .Where(path => path is not null)
                    .ToArray();
                if (expectedPaths.Length == 0 || expectedPaths.Any(path => !File.Exists(path)))
                    throw new InvalidDataException("恢复磁带不完整");
                validPendingIds.Add(start.ReplayId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[云回放] 清理无效恢复元数据 {Path.GetFileName(metadataPath)}：{ex.Message}");
                TryDeleteFile(metadataPath);
            }
        }
        foreach (var file in Directory.EnumerateFiles(_pendingRoot, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var marker = name.LastIndexOf(".p", StringComparison.Ordinal);
            var replayId = marker > 0 ? name[..marker] : "";
            if (!validPendingIds.Contains(replayId)) TryDeleteFile(file);
        }
        var referenced = new HashSet<string>(PathComparer());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT payload_path FROM cloud_replays;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) referenced.Add(ResolveInside(_root, reader.GetString(0)));
        }
        foreach (var file in Directory.EnumerateFiles(_payloadRoot, "*.json.gz", SearchOption.AllDirectories))
            if (!referenced.Contains(Path.GetFullPath(file))) TryDeleteFile(file);
    }

    private string PayloadPath(string account, string replayId)
    {
        var ownerKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(account.Trim().ToLowerInvariant()))).ToLowerInvariant()[..24];
        return ResolveInside(_payloadRoot, ownerKey, $"{replayId}.json.gz");
    }

    private string PendingMetadataPath(string replayId)
        => ResolveInside(_pendingRoot, $"{replayId}.meta.json");

    private static void WriteMetadataAtomic(string path, CloudReplayMatchStart start)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(start, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private void DeletePayload(string relativePath)
    {
        try { TryDeleteFile(ResolveInside(_root, relativePath)); }
        catch (Exception ex) { Console.Error.WriteLine($"[云回放] 删除载荷失败：{ex.Message}"); }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static bool TryReadMutation<T>(
        SqliteConnection connection,
        string owner,
        string requestId,
        string operation,
        out T value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation, response_json FROM cloud_replay_mutations
            WHERE owner_account = $owner AND request_id = $requestId;
            """;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$requestId", requestId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            value = default!;
            return false;
        }
        if (!string.Equals(reader.GetString(0), operation, StringComparison.Ordinal))
            throw new CloudReplayException("request_conflict", "同一 requestId 已被其他云回放操作使用。");
        value = JsonSerializer.Deserialize<T>(reader.GetString(1), JsonOptions)
            ?? throw new InvalidDataException("云回放幂等响应损坏。");
        return true;
    }

    private static void WriteMutation<T>(
        SqliteConnection connection,
        SqliteTransaction tx,
        string owner,
        string requestId,
        string operation,
        T response)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO cloud_replay_mutations (
                owner_account, request_id, operation, response_json, created_at_ms)
            VALUES ($owner, $requestId, $operation, $response, $createdAt);
            """;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$requestId", requestId);
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$response", JsonSerializer.Serialize(response, JsonOptions));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private static string CreateShareToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private string ResolveInside(string root, params string[] parts)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(parts.Aggregate(normalizedRoot, Path.Combine));
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, PathComparison()))
            throw new InvalidDataException("云回放路径越过存储根目录。");
        return candidate;
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsSafeReplayId(string value)
        => value.Length is >= 8 and <= 64 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');

    /// <summary>无副作用校验并规范化外部回放 ID，供任何持久化关联前复用。</summary>
    internal static bool TryNormalizeReplayId(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? "";
        return IsSafeReplayId(normalized);
    }

    private static string RequireReplayId(string value)
    {
        if (!TryNormalizeReplayId(value, out var normalized))
            throw new CloudReplayException("invalid_request", "回放 ID 无效。");
        return normalized;
    }

    private static string RequireAccount(string value)
    {
        value = value?.Trim() ?? "";
        if (value.Length is < 1 or > 80) throw new CloudReplayException("login_required", "请先登录。");
        return value;
    }

    private static string RequireRequestId(string value)
    {
        value = value?.Trim() ?? "";
        if (value.Length is < 8 or > 100 || value.Any(char.IsControl))
            throw new CloudReplayException("invalid_request", "requestId 无效。");
        return value;
    }

    private static void ValidatePlayer(CloudReplayPlayer player)
    {
        if (!player.Record) return;
        if (string.IsNullOrWhiteSpace(player.Account) || player.Account.Length > 80)
            throw new ArgumentException("云回放参战账号无效。");
        if (player.DisplayName.Length > 160)
            throw new ArgumentException("云回放玩家昵称过长。");
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool TryBoolean(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _initialized) == 0) Initialize();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(CloudReplayStore));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record CompletedView(
        string ReplayId,
        string OwnerAccount,
        int ParticipantIndex,
        string OpponentAccount,
        long StartedAt,
        long CompletedAt,
        string MyName,
        string OpponentName,
        string MyLeader,
        string OpponentLeader,
        bool WinnerIsMe,
        bool IsDraw,
        string Reason,
        int TurnCount,
        string MatchKind,
        string RuntimeArtifactId,
        string RuntimeManifestHash,
        string PayloadPath,
        long SizeBytes);

    private readonly record struct PendingTapeState(
        int FrameCount,
        int LastTick,
        bool HasTerminalFrame);
}

public sealed class CloudReplayCapture
{
    private readonly CloudReplayStore _store;
    private readonly int[] _frameCounts = new int[2];
    private readonly int[] _lastTicks = [-1, -1];
    private readonly bool[] _terminalFrames = new bool[2];
    private readonly object _frameGate = new();
    private readonly ConcurrentDictionary<(string Account, string FeedbackId), byte> _feedback = new();
    private readonly SemaphoreSlim _finishGate = new(1, 1);
    private string? _failureReason;
    private int _completed;
    private int _aborted;

    internal CloudReplayCapture(
        CloudReplayStore store,
        CloudReplayMatchStart start,
        string?[] keys,
        string?[] paths,
        string metadataPath,
        IReadOnlyList<int>? initialFrameCounts = null,
        IReadOnlyList<int>? initialLastTicks = null,
        IReadOnlyList<bool>? initialTerminalFrames = null)
    {
        _store = store;
        Start = start;
        Keys = keys;
        Paths = paths;
        MetadataPath = metadataPath;
        if (initialFrameCounts is not null)
            for (var index = 0; index < Math.Min(2, initialFrameCounts.Count); index++)
                _frameCounts[index] = Math.Max(0, initialFrameCounts[index]);
        if (initialLastTicks is not null)
            for (var index = 0; index < Math.Min(2, initialLastTicks.Count); index++)
                _lastTicks[index] = initialLastTicks[index];
        if (initialTerminalFrames is not null)
            for (var index = 0; index < Math.Min(2, initialTerminalFrames.Count); index++)
                _terminalFrames[index] = initialTerminalFrames[index];
    }

    internal CloudReplayMatchStart Start { get; }
    internal string?[] Keys { get; }
    internal string?[] Paths { get; }
    internal string MetadataPath { get; }
    internal string? FailureReason => Volatile.Read(ref _failureReason);
    internal bool IsAborted => Volatile.Read(ref _aborted) != 0;
    internal IEnumerable<(string Account, string FeedbackId)> FeedbackLinks
        => _feedback.Keys.Select(key => (key.Account, key.FeedbackId));

    public void AppendSnapshot(int playerIndex, object payload)
        => _store.Append(this, playerIndex, payload);

    public async Task CompleteAsync(CloudReplayCompletion completion)
    {
        await _finishGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _completed) != 0 || IsAborted) return;
            await _store.CompleteAsync(this, completion);
            Volatile.Write(ref _completed, 1);
        }
        finally
        {
            _finishGate.Release();
        }
    }

    public async Task AbortAsync()
    {
        await _finishGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _completed) != 0 || IsAborted) return;
            MarkAborted();
            await _store.AbortAsync(this);
            Volatile.Write(ref _completed, 1);
        }
        finally
        {
            _finishGate.Release();
        }
    }

    internal int RegisterFrame(int playerIndex, int tick, bool isTerminal)
    {
        lock (_frameGate)
        {
            if (_terminalFrames[playerIndex])
                throw new InvalidDataException("云回放终局帧之后不得继续追加状态。");
            if (tick <= _lastTicks[playerIndex])
                throw new InvalidDataException("云回放 Tick 必须严格递增。");
            _lastTicks[playerIndex] = tick;
            _terminalFrames[playerIndex] = isTerminal;
            return ++_frameCounts[playerIndex];
        }
    }

    internal CloudReplayRecoveryFrameState GetRecoveryFrameState(int playerIndex)
    {
        lock (_frameGate)
            return new CloudReplayRecoveryFrameState(
                Keys[playerIndex] is not null,
                _frameCounts[playerIndex],
                _lastTicks[playerIndex],
                _terminalFrames[playerIndex]);
    }
    internal void MarkFailed(string reason) => Interlocked.CompareExchange(ref _failureReason, reason, null);
    internal void MarkAborted() => Volatile.Write(ref _aborted, 1);

    internal bool TryAddFeedback(string account, string feedbackId)
    {
        var participant = string.Equals(account, Start.Player0.Account, StringComparison.OrdinalIgnoreCase)
            ? Start.Player0
            : string.Equals(account, Start.Player1.Account, StringComparison.OrdinalIgnoreCase)
                ? Start.Player1
                : null;
        return participant?.Record == true && _feedback.TryAdd((participant.Account, feedbackId), 0);
    }
}

internal readonly record struct CloudReplayRecoveryFrameState(
    bool Recorded,
    int FrameCount,
    int LastTick,
    bool HasTerminalFrame);
