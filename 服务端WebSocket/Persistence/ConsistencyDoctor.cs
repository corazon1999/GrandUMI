using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Persistence;

public sealed record SqliteSchemaOverview(
    string Name,
    string Path,
    bool Exists,
    bool Healthy,
    string Integrity,
    int UserVersion,
    IReadOnlyList<string> MigrationTables,
    long SizeBytes,
    long? LastWriteAt);

public sealed record ConsistencyDoctorSnapshot(
    long CheckedAt,
    int Processed,
    int Succeeded,
    int Retried,
    int OpenFindings,
    IReadOnlyDictionary<string, int> OutboxCounts,
    IReadOnlyList<SqliteSchemaOverview> Schemas);

/// <summary>
/// 明确负责玩家库昵称到共享账号目录的跨 SQLite 最终一致性。
/// 写入侧使用 players.db 内的事务 outbox；消费者幂等覆盖目录昵称，失败按租约和指数退避重试；
/// 巡检把差异写入管理员修复队列，不尝试伪装成跨任意数据库的通用事务框架。
/// </summary>
public sealed class ConsistencyDoctor
{
    private const string DisplayNameScope = "player_display_name_directory";
    private readonly PlayerDataStore _players;
    private readonly AccountAuthenticationStore _accounts;
    private readonly OperationsCenterStore _operations;
    private readonly IReadOnlyDictionary<string, string> _schemaPaths;

    public ConsistencyDoctor(
        PlayerDataStore players,
        AccountAuthenticationStore accounts,
        OperationsCenterStore operations,
        IReadOnlyDictionary<string, string>? schemaPaths = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _schemaPaths = schemaPaths ?? new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["players"] = players.DatabasePath,
            ["shared_accounts"] = accounts.DatabasePath,
            ["operations"] = operations.DatabasePath,
        };
    }

    public ConsistencyDoctorSnapshot RunOnce(DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var claimed = _players.ClaimDisplayNameSyncBatch(nowUtc: now);
        var succeeded = 0;
        var retried = 0;
        foreach (var item in claimed)
        {
            try
            {
                _accounts.UpdateDirectorySearchName(item.Account, item.DisplayName);
                _players.CompleteDisplayNameSync(item.Id, now);
                _operations.ResolveConsistencyFinding(DisplayNameScope, item.Account.ToUpperInvariant());
                succeeded++;
            }
            catch (Exception ex)
            {
                _players.RetryDisplayNameSync(item.Id, ex.Message, now);
                _operations.UpsertConsistencyFinding(
                    DisplayNameScope,
                    item.Account.ToUpperInvariant(),
                    "warning",
                    JsonSerializer.Serialize(new { account = item.Account, displayName = item.DisplayName, revision = item.Revision }),
                    JsonSerializer.Serialize(new { error = ex.Message, attempts = item.Attempts }),
                    "sync_display_name",
                    ex.Message);
                retried++;
            }
        }

        InspectDisplayNameDifferences();
        var findings = _operations.ListConsistencyFindings("open");
        return new ConsistencyDoctorSnapshot(
            new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            claimed.Count,
            succeeded,
            retried,
            findings.Count,
            _players.GetDisplayNameSyncOutboxCounts(),
            GetSchemaOverview());
    }

    public void QueueDisplayNameRepair(
        long findingId,
        string actorAccount,
        string source,
        string requestId)
    {
        var finding = _operations.ListConsistencyFindings(limit: 500)
            .SingleOrDefault(item => item.Id == findingId)
            ?? throw new OperationsCenterException("not_found", "一致性差异不存在。");
        if (!string.Equals(finding.Scope, DisplayNameScope, StringComparison.Ordinal)
            || !string.Equals(finding.RepairAction, "sync_display_name", StringComparison.Ordinal))
            throw new OperationsCenterException("manual_required", "该差异不能由自动昵称同步修复。");
        using var authority = JsonDocument.Parse(finding.AuthoritativeJson);
        var account = authority.RootElement.GetProperty("account").GetString()
            ?? throw new OperationsCenterException("invalid_finding", "差异缺少权威账号。");
        _players.QueueDisplayNameRepair(account, requestId);
        _operations.MarkConsistencyRepairQueued(findingId, actorAccount, source, requestId);
    }

    public IReadOnlyList<SqliteSchemaOverview> GetSchemaOverview()
        => _schemaPaths.Select(item => InspectSchema(item.Key, item.Value)).ToArray();

    public async Task RunLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(interval));
        while (!cancellationToken.IsCancellationRequested)
        {
            try { RunOnce(); }
            catch (Exception ex) { Console.Error.WriteLine($"[一致性 Doctor] 巡检失败：{ex.Message}"); }
            try { await Task.Delay(interval, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        }
    }

    private void InspectDisplayNameDifferences()
    {
        var local = _players.GetPlayerDirectoryEntries();
        var shared = _accounts.GetDirectorySearchNames();
        foreach (var entry in local)
        {
            var key = entry.Account.ToUpperInvariant();
            if (!shared.TryGetValue(entry.Account, out var observed))
            {
                _operations.UpsertConsistencyFinding(
                    DisplayNameScope,
                    key,
                    "critical",
                    JsonSerializer.Serialize(new { entry.Account, entry.DisplayName, entry.DisplayNameRevision }),
                    "{\"missing\":true}",
                    "manual_account_authority",
                    "共享账号目录中不存在该权威玩家账号；禁止自动创建。 ");
                continue;
            }
            if (!string.Equals(entry.DisplayName, observed, StringComparison.Ordinal))
            {
                _operations.UpsertConsistencyFinding(
                    DisplayNameScope,
                    key,
                    "warning",
                    JsonSerializer.Serialize(new { account = entry.Account, displayName = entry.DisplayName, revision = entry.DisplayNameRevision }),
                    JsonSerializer.Serialize(new { account = entry.Account, displayName = observed }),
                    "sync_display_name");
                continue;
            }
            _operations.ResolveConsistencyFinding(DisplayNameScope, key);
        }
    }

    private static SqliteSchemaOverview InspectSchema(string name, string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path)) return new SqliteSchemaOverview(name, path, false, false, "missing", 0, [], 0, null);
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            string integrity;
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                integrity = check.ExecuteScalar() as string ?? "unknown";
            }
            int version;
            using (var readVersion = connection.CreateCommand())
            {
                readVersion.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(readVersion.ExecuteScalar());
            }
            var migrations = new List<string>();
            using (var readTables = connection.CreateCommand())
            {
                readTables.CommandText = """
                    SELECT name FROM sqlite_master
                    WHERE type='table' AND (name LIKE '%migration%' OR name LIKE '%schema%')
                    ORDER BY name;
                    """;
                using var reader = readTables.ExecuteReader();
                while (reader.Read()) migrations.Add(reader.GetString(0));
            }
            var info = new FileInfo(path);
            return new SqliteSchemaOverview(name, path, true,
                string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase), integrity,
                version, migrations, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
        }
        catch (Exception ex)
        {
            var info = new FileInfo(path);
            return new SqliteSchemaOverview(name, path, true, false, ex.Message, 0, [], info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
        }
    }
}
