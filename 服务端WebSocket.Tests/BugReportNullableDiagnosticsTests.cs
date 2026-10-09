using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

[Collection("权威在线会话隔离")]
public sealed class BugReportNullableDiagnosticsTests
{
    private static readonly string[] ConnectionIntegerFields =
        ["connectionGeneration", "reconnectCount", "endpointFailureCount", "stateDeltaCount", "fullStateCount", "maxMessageQueueDepth"];
    private static readonly string[] ConnectionNumberFields =
        ["handshakeMs", "rttMs", "rttP95Ms", "actionRoundTripMs", "actionRoundTripP95Ms"];

    [Theory]
    [InlineData("null")]
    [InlineData("\"未测得\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData(null)]
    public void 非数值或缺失的诊断字段_按未知处理并保留其他有效信息(string? invalidJson)
    {
        foreach (var (section, field) in ConnectionIntegerFields.Concat(ConnectionNumberFields)
                     .Select(field => ("connection", field))
                     .Concat(new[] { ("viewport", "width"), ("viewport", "height"), ("viewport", "devicePixelRatio") }))
        {
            var submitted = BuildStructuredEvidence();
            var target = submitted[section]!.AsObject();
            if (invalidJson is null) target.Remove(field);
            else target[field] = JsonNode.Parse(invalidJson);

            var normalized = FeedbackEvidenceSanitizer.Sanitize(JsonSerializer.SerializeToElement(submitted), null);

            Assert.Null(normalized[section]![field]);
            Assert.Equal("connected", normalized["connection"]!["state"]!.GetValue<string>());
            Assert.Equal("lobby", normalized["client"]!["context"]!.GetValue<string>());
        }
    }

    [Fact]
    public void 有效数值仍按原来的上下限保存()
    {
        var submitted = BuildStructuredEvidence();
        submitted["connection"]!["reconnectCount"] = -2;
        submitted["connection"]!["rttMs"] = 3_600_001d;
        submitted["viewport"]!["width"] = 20_001;
        submitted["viewport"]!["devicePixelRatio"] = 9d;

        var normalized = FeedbackEvidenceSanitizer.Sanitize(JsonSerializer.SerializeToElement(submitted), null);

        Assert.Equal(0L, normalized["connection"]!["reconnectCount"]!.GetValue<long>());
        Assert.Equal(3_600_000d, normalized["connection"]!["rttMs"]!.GetValue<double>());
        Assert.Equal(20_000L, normalized["viewport"]!["width"]!.GetValue<long>());
        Assert.Equal(8d, normalized["viewport"]!["devicePixelRatio"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("bug", false)]
    [InlineData("suggestion", false)]
    [InlineData("bug", true)]
    [InlineData("suggestion", true)]
    public async Task 未产生网络测量时_真实反馈处理返回成功并幂等保存Case证据和文件(string category, bool legacy)
    {
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("反馈回归必须设置 E 盘测试临时目录。");
        var workspace = Path.Combine(root, "feedback-nullable-diagnostics", Guid.NewGuid().ToString("N"));
        var sessions = (ConcurrentDictionary<string, WsSession>)typeof(WebSocketBridge)
            .GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var operationsField = typeof(WebSocketBridge).GetField("_operationsCenterStore", BindingFlags.Static | BindingFlags.NonPublic)!;
        var reportsRootField = typeof(BugReportStore).GetField("_root", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousOperations = operationsField.GetValue(null);
        var previousReportsRoot = reportsRootField.GetValue(null);
        using var operations = new OperationsCenterStore(Path.Combine(workspace, "operations-center.db"));
        var session = new WsSession();
        var replies = new List<JsonElement>();
        session.StartSender(message =>
        {
            replies.Add(JsonSerializer.SerializeToElement(message.Data));
            return Task.CompletedTask;
        });
        sessions[session.SessionId] = session;
        try
        {
            operations.Initialize();
            operationsField.SetValue(null, operations);
            reportsRootField.SetValue(null, Path.Combine(workspace, "reports"));
            var submitted = BuildStructuredEvidence();
            // 未打出游戏动作或尚未收到 Ping 回包时，客户端正常提交这些空值。
            foreach (var field in ConnectionNumberFields) submitted["connection"]![field] = null;
            var message = new Dictionary<string, JsonElement>
            {
                ["category"] = JsonSerializer.SerializeToElement(category),
                ["description"] = JsonSerializer.SerializeToElement("测试\n期望能够提交反馈"),
                ["requestId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D")),
            };
            if (legacy)
                message["clientInfo"] = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["meta"] = new JsonObject
                    {
                        ["context"] = "lobby",
                        ["connectionState"] = "connected",
                        ["networkDiagnostics"] = submitted["connection"]!.DeepClone(),
                    },
                }.ToJsonString());
            else message["clientEvidence"] = JsonSerializer.SerializeToElement(submitted);

            var handler = typeof(WebSocketBridge).GetMethod("OnBugReportAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)handler.Invoke(null, [session, message])!;
            await (Task)handler.Invoke(null, [session, message])!;
            await session.StopSenderAsync();

            Assert.Equal(2, replies.Count);
            Assert.All(replies, reply => Assert.True(reply.GetProperty("result").GetBoolean()));
            var caseId = replies[0].GetProperty("caseId").GetString()!;
            Assert.Equal(caseId, replies[1].GetProperty("caseId").GetString());
            Assert.Single(operations.ListCases(new OperationsCaseQuery()).Items);
            var detail = operations.GetCase(caseId);
            Assert.Equal(category, detail.Summary.Category);
            Assert.Equal("测试\n期望能够提交反馈", detail.Description);
            var evidence = JsonNode.Parse(Assert.Single(detail.Evidence).PayloadJson)!;
            Assert.Null(evidence["client"]!["connection"]!["actionRoundTripMs"]);
            Assert.Equal("not_in_room", evidence["authority"]!["captureStatus"]!.GetValue<string>());
            var reportPath = Assert.Single(Directory.GetFiles(Path.Combine(workspace, "reports"), "*.json", SearchOption.AllDirectories));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(detail.ExternalEventId, report.RootElement.GetProperty("feedbackId").GetString());
        }
        finally
        {
            sessions.TryRemove(session.SessionId, out _);
            await session.StopSenderAsync();
            operationsField.SetValue(null, previousOperations);
            reportsRootField.SetValue(null, previousReportsRoot);
            operations.Dispose();
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
    }

    private static JsonObject BuildStructuredEvidence() => JsonNode.Parse("""
        {
          "schema": "grandumi.feedback.client.v1",
          "client": {"version": "unknown", "commit": "unknown", "context": "lobby"},
          "connection": {
            "state": "connected", "endpointHost": "test.grand-umi.com",
            "connectionGeneration": 1, "reconnectCount": 0, "endpointFailureCount": 0,
            "handshakeMs": 25, "rttMs": 20, "rttP95Ms": 30,
            "actionRoundTripMs": 50, "actionRoundTripP95Ms": 60,
            "stateDeltaCount": 0, "fullStateCount": 1, "maxMessageQueueDepth": 0
          },
          "viewport": {"width": 390, "height": 844, "orientation": "portrait", "devicePixelRatio": 3}
        }
        """)!.AsObject();
}
