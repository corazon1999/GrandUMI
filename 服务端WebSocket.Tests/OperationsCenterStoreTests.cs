using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class OperationsCenterStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _operationsPath;
    private readonly OperationsCenterStore _operations;

    public OperationsCenterStoreTests()
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(tempRoot))
            throw new InvalidOperationException(
                "运营中心测试必须先通过 ops/windows/GrandUmiTemp.ps1 设置 GRANDUMI_TEST_TEMP_ROOT。");
        _tempDirectory = Path.Combine(Path.GetFullPath(tempRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _operationsPath = Path.Combine(_tempDirectory, "operations-center.db");
        _operations = new OperationsCenterStore(_operationsPath);
        _operations.Initialize();
    }

    [Fact]
    public void Case支持幂等创建状态流转关联证据和首响P90()
    {
        var requestId = RequestId();
        var input = new OperationsCaseCreate(
            OperationsCaseSources.PlayerReport,
            "harassment",
            "对局聊天举报",
            "玩家举报了对局聊天内容。",
            "Reporter",
            "Subject",
            "Opponent",
            "room-001",
            "replay-001",
            "message-001",
            requestId,
            [
                new OperationsCaseEvidenceInput("game_chat", "{\"text\":\"证据\"}", DateTime.UtcNow.AddMinutes(-1)),
                new OperationsCaseEvidenceInput("report_context", "{\"turn\":5}")
            ],
            "high");

        var caseId = _operations.CreateCase(input);
        Assert.Equal(caseId, _operations.CreateCase(input));
        var createConflict = Assert.Throws<OperationsCenterException>(() =>
            _operations.CreateCase(input with { Description = "同一请求编号被篡改后的描述。" }));
        Assert.Equal("request_conflict", createConflict.Code);

        var created = _operations.GetCase(caseId);
        Assert.Equal("room-001", created.Summary.RoomId);
        Assert.Equal("replay-001", created.Summary.ReplayId);
        Assert.Equal("message-001", created.ExternalEventId);
        Assert.Single(created.Evidence);
        Assert.Equal("report_context", created.Evidence[0].Type);

        var transitioned = _operations.TransitionCase(
            "Admin", "web_admin", caseId, "triaged", "Operator", "进入人工核查", "已接单", RequestId());
        Assert.Equal("triaged", transitioned.Summary.Status);
        Assert.Equal("Operator", transitioned.Summary.Assignee);
        Assert.Equal("进入人工核查", transitioned.Summary.Disposition);
        Assert.NotNull(transitioned.Summary.FirstActionAt);

        var page = _operations.ListCases(new OperationsCaseQuery(Status: "triaged", Account: "subject"));
        Assert.Equal(1, page.Total);
        Assert.Equal(caseId, Assert.Single(page.Items).CaseId);

        var metrics = _operations.GetCaseMetrics(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        Assert.Equal(1, metrics.Total);
        Assert.Equal(0, metrics.AwaitingFirstAction);
        Assert.NotNull(metrics.FirstActionP90Milliseconds);
    }

    [Fact]
    public void 申诉只允许被处罚账号且重复请求不重复写入()
    {
        var caseId = CreateCase(subject: "Subject");
        _operations.TransitionCase(
            "Admin", "web_admin", caseId, "triaged", null, null, "已分诊", RequestId());
        _operations.TransitionCase(
            "Admin", "web_admin", caseId, "resolved", null, "已处理", "已处理", RequestId());

        var forbidden = Assert.Throws<OperationsCenterException>(() =>
            _operations.SubmitAppeal("OtherAccount", caseId, "这不是该账号可提交的申诉说明。", RequestId()));
        Assert.Equal("forbidden", forbidden.Code);

        var requestId = RequestId();
        var appealed = _operations.SubmitAppeal("subject", caseId, "我希望管理员复核这次处罚与证据。", requestId);
        var replayed = _operations.SubmitAppeal("SUBJECT", caseId, "我希望管理员复核这次处罚与证据。", requestId);
        Assert.Equal("appealed", appealed.Summary.Status);
        Assert.Equal(appealed.Summary.CaseId, replayed.Summary.CaseId);
        Assert.Single(appealed.Events.Where(item => item.EventType == "appeal_submitted"));

        var otherCase = CreateCase(subject: "Subject");
        var conflict = Assert.Throws<OperationsCenterException>(() =>
            _operations.SubmitAppeal("Subject", otherCase, "尝试复用另一个 Case 的请求编号。", requestId));
        Assert.Equal("request_conflict", conflict.Code);
    }

    [Fact]
    public void 处罚必须限时并可查询到期和撤销状态()
    {
        var caseId = CreateCase(subject: "Subject");
        var noPermanentPenalty = Assert.Throws<OperationsCenterException>(() =>
            _operations.ApplyPenalty(
                "Admin", "web_admin", caseId, "Subject", OperationsPenaltyKinds.Mute,
                DateTime.MaxValue, "聊天违规", RequestId()));
        Assert.Equal("invalid_expiry", noPermanentPenalty.Code);

        var expires = DateTime.UtcNow.AddHours(2);
        var requestId = RequestId();
        var penalty = _operations.ApplyPenalty(
            "Admin", "web_admin", caseId, "Subject", OperationsPenaltyKinds.MatchBan,
            expires, "恶意中断对局", requestId);
        var replayed = _operations.ApplyPenalty(
            "Admin", "web_admin", caseId, "Subject", OperationsPenaltyKinds.MatchBan,
            expires, "恶意中断对局", requestId);
        Assert.Equal(penalty.PenaltyId, replayed.PenaltyId);
        var requestConflict = Assert.Throws<OperationsCenterException>(() =>
            _operations.ApplyPenalty(
                "Admin", "web_admin", caseId, "OtherSubject", OperationsPenaltyKinds.MatchBan,
                expires, "恶意中断对局", requestId));
        Assert.Equal("request_conflict", requestConflict.Code);
        Assert.True(_operations.GetRestrictions("subject").MatchBanned);
        Assert.False(_operations.GetRestrictions("subject", DateTime.UtcNow.AddHours(3)).MatchBanned);

        var revoked = _operations.RevokePenalty(
            "Admin", "web_admin", penalty.PenaltyId, "复核后撤销", RequestId());
        Assert.NotNull(revoked.RevokedAt);
        Assert.False(_operations.GetRestrictions("Subject").MatchBanned);
        Assert.Contains(_operations.ListPrivilegedAudit(), item =>
            item.Operation == "penalty_apply" && item.Target == "Subject" && item.Result == "success");
    }

    [Fact]
    public void 特权审计不可改写且高风险确认拒绝QQ并只能消费一次()
    {
        var denied = Assert.Throws<OperationsCenterException>(() =>
            _operations.IssueHighRiskChallenge(
                "Admin", "qq_agent", "deploy_test", "test", RequestId()));
        Assert.Equal("forbidden_source", denied.Code);

        var challenge = _operations.IssueHighRiskChallenge(
            "Admin", "web_admin", "deploy_test", "test", RequestId());
        var wrongTarget = Assert.Throws<OperationsCenterException>(() =>
            _operations.ConsumeHighRiskChallenge(
                "Admin", "web_admin", "deploy_test", "production", challenge.ChallengeId, challenge.ConfirmationToken));
        Assert.Equal("confirmation_invalid", wrongTarget.Code);

        _operations.ConsumeHighRiskChallenge(
            "Admin", "web_admin", "deploy_test", "test", challenge.ChallengeId, challenge.ConfirmationToken);
        var replay = Assert.Throws<OperationsCenterException>(() =>
            _operations.ConsumeHighRiskChallenge(
                "Admin", "web_admin", "deploy_test", "test", challenge.ChallengeId, challenge.ConfirmationToken));
        Assert.Equal("confirmation_invalid", replay.Code);

        var hexTarget = "production:draft-4:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var hexChallenge = _operations.IssueHighRiskChallenge(
            "Admin", "web_admin", "publish_hex_catalog", hexTarget, RequestId());
        _operations.ConsumeHighRiskChallenge(
            "Admin", "web_admin", "publish_hex_catalog", hexTarget,
            hexChallenge.ChallengeId, hexChallenge.ConfirmationToken);
        Assert.True(_operations.VerifyAuditChain());

        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={_operationsPath};Pooling=False");
        connection.Open();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE privileged_audit_events SET result='tampered' WHERE id=(SELECT MIN(id) FROM privileged_audit_events);";
        Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM privileged_audit_events;";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
    }

    [Fact]
    public void 昵称跨库同步通过Outbox重试并由一致性Doctor核验()
    {
        var playersPath = Path.Combine(_tempDirectory, "players.db");
        var accountsPath = Path.Combine(_tempDirectory, "accounts.db");
        var players = new PlayerDataStore(playersPath);
        players.Initialize();
        players.Login("Alice");
        var shared = new SharedAccountDatabase(accountsPath);
        shared.Initialize([new LegacyAccountSource("local", playersPath, Authoritative: true)]);
        var accounts = new AccountAuthenticationStore(players, shared);

        players.AdminRenamePlayer("Admin", "Alice", "新昵称");
        Assert.Equal(1, players.GetDisplayNameSyncOutboxCounts()["pending"]);

        var claimed = Assert.Single(players.ClaimDisplayNameSyncBatch(nowUtc: DateTime.UtcNow));
        players.RetryDisplayNameSync(claimed.Id, "模拟共享库暂时不可用", DateTime.UtcNow);
        Assert.Equal(1, players.GetDisplayNameSyncOutboxCounts()["retry"]);

        var doctor = new ConsistencyDoctor(players, accounts, _operations);
        var snapshot = doctor.RunOnce(DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, snapshot.Succeeded);
        Assert.Equal(0, snapshot.Retried);
        Assert.Equal("新昵称", accounts.GetDirectorySearchNames()["Alice"]);
        Assert.Equal(1, snapshot.OutboxCounts["done"]);
        Assert.Empty(_operations.ListConsistencyFindings("open"));
        Assert.All(snapshot.Schemas, schema =>
        {
            Assert.True(schema.Exists);
            Assert.True(schema.Healthy, $"{schema.Name}: {schema.Integrity}");
        });

        var replay = doctor.RunOnce(DateTime.UtcNow.AddMinutes(2));
        Assert.Equal(0, replay.Processed);
        Assert.Equal("新昵称", accounts.GetDirectorySearchNames()["Alice"]);
    }

    public void Dispose()
    {
        _operations.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private string CreateCase(string subject = "Subject")
        => _operations.CreateCase(new OperationsCaseCreate(
            OperationsCaseSources.Manual,
            "moderation",
            "人工处置 Case",
            "用于验证运营中心状态与权限边界。",
            "Reporter",
            subject,
            null,
            null,
            null,
            null,
            RequestId()));

    private static string RequestId() => Guid.NewGuid().ToString("D");
}
