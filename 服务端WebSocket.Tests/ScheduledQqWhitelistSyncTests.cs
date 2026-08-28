using System.Net;
using System.Text.Json;
using GrandUMI.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ScheduledQqWhitelistSyncTests : IDisposable
{
    private const string GroupId = "297542853";
    private const string GroupName = "GrandUMI测试群";
    private const string ClientA = "11111111-1111-1111-1111-111111111111";
    private const string ClientB = "22222222-2222-2222-2222-222222222222";
    private readonly string _directory;
    private readonly string _databasePath;

    public ScheduledQqWhitelistSyncTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "整点白名单同步测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _directory = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_directory, "accounts.db");
        new SharedAccountDatabase(_databasePath).Initialize();
    }

    [Fact]
    public void ScheduledSync_原子替换并复用版本审计与操作者语义()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001", "10002"));
        var hour = Hour(15);

        var result = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10002", "10003", "10004"),
            GroupId, GroupName, 1, 25, 600, hour + 10);

        Assert.False(result.Replayed);
        Assert.True(result.NotificationOwner);
        Assert.Equal(2, result.Import.Version);
        Assert.Equal(3, result.Import.MemberCount);
        Assert.Equal(2, result.Import.AddedCount);
        Assert.Equal(1, result.Import.RemovedCount);
        var status = store.GetStatus();
        Assert.Equal(2, status.Version);
        Assert.Equal($"qq-sync:{GroupId}", status.ImportedBy);

        using var connection = OpenConnection();
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_qq_whitelist_sync_runs;"));
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_qq_whitelist_import_audit;"));
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_account_security_events WHERE event_type='whitelist_replaced';"));
        Assert.Equal(3L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_qq_whitelist_members WHERE version=2;"));
        Assert.Equal(2L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_qq_whitelist_update_events WHERE outcome='success';"));
        var latest = Assert.Single(store.GetRecentWhitelistUpdateEvents().Take(1));
        Assert.Equal("success", latest.Outcome);
        Assert.Equal(result.OperationKey, latest.OperationKey);
        Assert.Equal(2, latest.Version);
        Assert.Equal(3, latest.MemberCount);
    }

    [Fact]
    public async Task ConcurrentInstances_同一整点只有一个版本和一个通知所有者()
    {
        var firstStore = CreateStore();
        firstStore.Import("手工管理员", MembersJson("10001", "10002"));
        var secondStore = CreateStore();
        var hour = Hour(16);
        using var start = new ManualResetEventSlim(false);
        var attempts = new[] { ClientA, ClientB }.Select(client => Task.Run(() =>
        {
            start.Wait();
            return (client == ClientA ? firstStore : secondStore).SynchronizeScheduledGroup(
                Request(hour, client, "10001", "10002", "10003"),
                GroupId, GroupName, 1, 25, 600, hour + 10);
        })).ToArray();
        start.Set();

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => !result.Replayed);
        Assert.Single(results, result => result.NotificationOwner);
        Assert.Equal(2, firstStore.GetStatus().Version);
        using var connection = OpenConnection();
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM shared_qq_whitelist_sync_runs;"));
    }

    [Fact]
    public void Replay_相同请求不递增版本且不同快照冲突()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001"));
        var hour = Hour(17);
        var first = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 1);
        var replay = store.SynchronizeScheduledGroup(
            Request(hour, ClientB, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 3600);

        Assert.True(replay.Replayed);
        Assert.False(replay.NotificationOwner);
        Assert.Equal(first.Import.Version, replay.Import.Version);
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10003"),
            GroupId, GroupName, 1, 25, 600, hour + 2));
        Assert.Equal(2, store.GetStatus().Version);
    }

    [Fact]
    public void Validation_空重复人数错误群过期和异常缩水均保留旧版本()
    {
        var store = CreateStore();
        var original = Enumerable.Range(10000, 100).Select(value => value.ToString()).ToArray();
        store.Import("手工管理员", MembersJson(original));
        var hour = Hour(18);

        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA), GroupId, GroupName, 1, 25, 600, hour + 1));
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10001"),
            GroupId, GroupName, 1, 25, 600, hour + 1));
        var wrongCount = Request(hour, ClientA, "10001", "10002") with
        {
            ReportedMemberCount = 3,
        };
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            wrongCount, GroupId, GroupName, 1, 25, 600, hour + 1));
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001"), "123456789", GroupName, 1, 25, 600, hour + 1));
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001"), GroupId, "错误群名", 1, 25, 600, hour + 1));
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, original), GroupId, GroupName, 1, 25, 600, hour + 601));
        Assert.Throws<QqAccessValidationException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, original.Take(70).ToArray()),
            GroupId, GroupName, 1, 25, 600, hour + 1));

        Assert.Equal(1, store.GetStatus().Version);
        Assert.Equal(100, store.GetStatus().MemberCount);
    }

    [Fact]
    public void SyncRunInsertFailure_整个白名单版本审计与成员替换一并回滚()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001", "10002"));
        using (var connection = OpenConnection())
        {
            using var trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER fail_qq_sync_run BEFORE INSERT ON shared_qq_whitelist_sync_runs
                BEGIN SELECT RAISE(ABORT, 'injected sync failure'); END;
                """;
            trigger.ExecuteNonQuery();
        }
        var hour = Hour(19);

        Assert.Throws<SqliteException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10002", "10003"),
            GroupId, GroupName, 1, 25, 600, hour + 1));

        Assert.Equal(1, store.GetStatus().Version);
        Assert.Equal(2, store.GetStatus().MemberCount);
        using var verify = OpenConnection();
        Assert.Equal(1L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_import_audit;"));
        Assert.Equal(0L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_sync_runs;"));
        Assert.Equal(1L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_update_events;"));
        Assert.Equal(1L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_members WHERE qq='10001';"));
    }

    [Fact]
    public void UpdateEventInsertFailure_白名单替换和整点记录全部回滚()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001", "10002"));
        using (var connection = OpenConnection())
        {
            using var trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER fail_qq_update_event
                BEFORE INSERT ON shared_qq_whitelist_update_events
                BEGIN SELECT RAISE(ABORT, 'injected update event failure'); END;
                """;
            trigger.ExecuteNonQuery();
        }
        var hour = Hour(19);

        Assert.Throws<SqliteException>(() => store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10002", "10003"),
            GroupId, GroupName, 1, 25, 600, hour + 1));

        Assert.Equal(1, store.GetStatus().Version);
        Assert.Equal(2, store.GetStatus().MemberCount);
        using var verify = OpenConnection();
        Assert.Equal(1L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_update_events;"));
        Assert.Equal(0L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_sync_runs;"));
        Assert.Equal(1L, Scalar(verify,
            "SELECT COUNT(*) FROM shared_qq_whitelist_members WHERE qq='10001';"));
    }

    [Fact]
    public void FailureReport_重复请求幂等且记录当前保留版本与失败原因()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001", "10002"));
        var hour = Hour(20);
        var request = new QqWhitelistScheduledFailureRequest(
            QqAccessStore.BuildScheduledSyncOperationKey(GroupId, hour),
            hour,
            GroupId,
            GroupName,
            ClientA,
            "OneBot 群成员接口暂时不可用");

        var first = store.ReportScheduledGroupFailure(
            request, GroupId, GroupName, hour + 700);
        var replay = store.ReportScheduledGroupFailure(
            request with { Error = "重复请求不得改写首个失败原因" },
            GroupId, GroupName, hour + 3600);

        Assert.Null(first.Committed);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Failure, replay.Failure);
        Assert.Equal(1, first.Failure!.Version);
        Assert.Equal(2, first.Failure.MemberCount);
        Assert.Equal("OneBot 群成员接口暂时不可用", first.Failure.Error);
        Assert.Equal(2, store.GetRecentWhitelistUpdateEvents().Count);
    }

    [Fact]
    public void FailureReport_响应丢失但服务端已提交时返回权威提交且不伪造失败()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001"));
        var hour = Hour(20);
        var synced = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 1);

        var result = store.ReportScheduledGroupFailure(
            new QqWhitelistScheduledFailureRequest(
                synced.OperationKey,
                hour,
                GroupId,
                GroupName,
                ClientA,
                "提交响应连接中断"),
            GroupId,
            GroupName,
            hour + 700);

        Assert.NotNull(result.Committed);
        Assert.Null(result.Failure);
        Assert.Equal(synced.Import.Version, result.Committed!.Import.Version);
        Assert.DoesNotContain(
            store.GetRecentWhitelistUpdateEvents(),
            update => update.Outcome == "failure");
    }

    [Fact]
    public void ManualImport_与整点幂等重放保持兼容()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001"));
        var hour = Hour(20);
        var scheduled = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 1);
        var manual = store.Import("应急管理员", MembersJson("10001", "10002", "10003"));
        var replay = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 4000);

        Assert.Equal(2, scheduled.Import.Version);
        Assert.Equal(3, manual.Version);
        Assert.Equal(2, replay.Import.Version);
        Assert.Equal(3, store.GetStatus().Version);
    }

    [Fact]
    public void NotificationAck_仅所有者可确认且确认本身幂等()
    {
        var store = CreateStore();
        store.Import("手工管理员", MembersJson("10001"));
        var hour = Hour(21);
        var synced = store.SynchronizeScheduledGroup(
            Request(hour, ClientA, "10001", "10002"),
            GroupId, GroupName, 1, 25, 600, hour + 1);

        Assert.Throws<QqAccessValidationException>(() =>
            store.AcknowledgeScheduledGroupNotification(
                synced.OperationKey, ClientB, synced.Import.Version));
        var acknowledged = store.AcknowledgeScheduledGroupNotification(
            synced.OperationKey, ClientA, synced.Import.Version);
        var replay = store.AcknowledgeScheduledGroupNotification(
            synced.OperationKey, ClientA, synced.Import.Version);

        Assert.NotNull(acknowledged.NotificationAcknowledgedAt);
        Assert.False(acknowledged.NotificationOwner);
        Assert.Equal(acknowledged.NotificationAcknowledgedAt, replay.NotificationAcknowledgedAt);
        Assert.False(store.GetScheduledGroupSync(synced.OperationKey, ClientA)!.NotificationOwner);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private QqAccessStore CreateStore()
        => new(new SharedAccountDatabase(_databasePath));

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long Hour(int hour)
        => new DateTimeOffset(2026, 8, 27, hour, 0, 0, TimeSpan.FromHours(8))
            .ToUnixTimeSeconds();

    private static string MembersJson(params string[] members)
        => JsonSerializer.Serialize(members);

    private static QqWhitelistScheduledSyncRequest Request(
        long hour,
        string client,
        params string[] members)
        => new(
            QqAccessStore.BuildScheduledSyncOperationKey(GroupId, hour),
            hour,
            GroupId,
            GroupName,
            members.Length,
            client,
            MembersJson(members));
}

public sealed class QqWhitelistSyncAuthorizationTests
{
    [Fact]
    public void InternalEndpoint_数据库忙属于可重试服务失败而非永久业务冲突()
    {
        Assert.True(QqWhitelistSyncHttpEndpoint.IsTransientStorageFailure(
            new QqAccessValidationException(
                "共享账号数据库正忙，请稍后重试。",
                new SqliteException("database is locked", 5))));
        Assert.False(QqWhitelistSyncHttpEndpoint.IsTransientStorageFailure(
            new QqAccessValidationException("群成员快照无效。")));
    }

    [Fact]
    public void InternalEndpoint_同时要求本机代理标记与抗时序密钥校验()
    {
        const string secret = "0123456789abcdef0123456789abcdef";
        var options = QqWhitelistSyncOptions.CreateForTests(
            "297542853", "GrandUMI测试群", "expected-proxy", secret);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-GrandUMI-Internal-Source"] = "expected-proxy";
        context.Request.Headers.Authorization = $"Bearer {secret}";
        Assert.True(options.IsAuthorized(context));

        context.Request.Headers.Authorization = "Bearer wrong-secret-with-enough-length";
        Assert.False(options.IsAuthorized(context));
        context.Request.Headers.Authorization = $"Bearer {secret}";
        context.Request.Headers["X-GrandUMI-Internal-Source"] = "wrong-proxy";
        Assert.False(options.IsAuthorized(context));
        context.Request.Headers["X-GrandUMI-Internal-Source"] = "expected-proxy";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        Assert.False(options.IsAuthorized(context));
    }
}
