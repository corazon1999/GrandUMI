using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class BugReportPersistenceTests
{
    [Fact]
    public void 正常反馈输入_按真实处理顺序写入统一Case和兼容文件()
    {
        var workspace = NewWorkspace();
        try
        {
            using var operations = new OperationsCenterStore(Path.Combine(workspace, "operations-center.db"));
            operations.Initialize();
            var identity = FeedbackRequestIdentityFactory.Create(
                "tester",
                "session-a",
                "5e0ba7d4-7e2d-4d48-b6dd-e5e0b4097a61");
            var submitted = JsonSerializer.SerializeToElement(new
            {
                schema = "grandumi.feedback.client.v1",
                capturedAtUtc = "2026-09-28T00:00:00Z",
                client = new { version = "0.999", commit = "unknown", context = "lobby" },
                connection = new
                {
                    state = "connected",
                    endpointHost = "test.grand-umi.com",
                    connectionGeneration = 1,
                    reconnectCount = 0,
                    endpointFailureCount = 0,
                    stateDeltaEnabled = true,
                    stateDeltaCount = 0,
                    fullStateCount = 1,
                    maxMessageQueueDepth = 0,
                },
                viewport = new
                {
                    width = 582,
                    height = 497,
                    orientation = "landscape",
                    devicePixelRatio = 1d,
                    standalone = false,
                    online = true,
                },
            });
            var clientEvidence = FeedbackEvidenceSanitizer.Sanitize(submitted, null);
            var authorityEvidence = new JsonObject
            {
                ["schema"] = "grandumi.feedback.authority.v1",
                ["trust"] = "server_authoritative",
                ["captureStatus"] = "not_in_room",
            };
            var evidence = new
            {
                schema = "grandumi.feedback.evidence.v1",
                authority = authorityEvidence,
                client = clientEvidence,
            };
            var evidenceJson = JsonSerializer.Serialize(evidence);
            var report = new
            {
                schema = "grandumi.feedback.report.v1",
                feedbackId = identity.FeedbackId,
                savedAtUtc = "2026-09-28T00:00:00.0000000Z",
                category = "bug",
                description = "测试",
                replayId = (string?)null,
                evidence,
            };

            var caseId = operations.CreateCase(new OperationsCaseCreate(
                OperationsCaseSources.BugReport,
                "bug",
                "玩家 Bug 反馈",
                "测试",
                null,
                null,
                null,
                null,
                null,
                identity.FeedbackId,
                identity.SourceRequestId,
                [new OperationsCaseEvidenceInput("bug_report_evidence_v1", evidenceJson)],
                "high"));
            var reportPath = BugReportStore.SaveAtRoot(
                report,
                Path.Combine(workspace, "reports"),
                identity.FeedbackId,
                "bug");

            var detail = operations.GetCase(caseId);
            Assert.Equal(identity.FeedbackId, detail.ExternalEventId);
            Assert.Equal("测试", detail.Description);
            Assert.Single(detail.Evidence);
            Assert.True(File.Exists(reportPath));
            using var persisted = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(identity.FeedbackId, persisted.RootElement.GetProperty("feedbackId").GetString());
        }
        finally
        {
            TryDelete(workspace);
        }
    }

    [Fact]
    public void 保存失败诊断_包含关联编号阶段异常类型和Sqlite代码()
    {
        var sqlite = new SqliteException("database is locked", 5, 5);
        var message = WebSocketBridge.FormatBugReportFailure(
            "a1b2c3d4e5f6",
            "create_case",
            sqlite);

        Assert.Contains("failureId=a1b2c3d4e5f6", message, StringComparison.Ordinal);
        Assert.Contains("stage=create_case", message, StringComparison.Ordinal);
        Assert.Contains("type=Microsoft.Data.Sqlite.SqliteException", message, StringComparison.Ordinal);
        Assert.Contains("sqliteCode=5", message, StringComparison.Ordinal);
        Assert.Contains("sqliteExtendedCode=5", message, StringComparison.Ordinal);
    }

    private static string NewWorkspace()
    {
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("反馈持久化测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");
        var workspace = Path.Combine(root, "bug-report-persistence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }
}
