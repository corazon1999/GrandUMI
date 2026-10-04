using System.Text;
using System.Text.Json;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

public sealed class BugReportMultilineTests : IDisposable
{
    private readonly string _workspace;
    private readonly OperationsCenterStore _operations;

    public BugReportMultilineTests()
    {
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("反馈测试必须通过 GrandUmiTemp.ps1 设置临时目录。");
        _workspace = Path.Combine(root, "bug-report-multiline", Guid.NewGuid().ToString("N"));
        _operations = new OperationsCenterStore(Path.Combine(_workspace, "operations-center.db"));
        _operations.Initialize();
    }

    [Theory]
    [InlineData("bug", "\n")]
    [InlineData("bug", "\r\n")]
    [InlineData("bug", "\r")]
    [InlineData("suggestion", "\n")]
    [InlineData("suggestion", "\r\n")]
    public void 多行反馈与建议_完整保存描述证据且重复提交只产生一条记录(string category, string newline)
    {
        var description = string.Join(newline,
            "期望：能够正常发动场上效果",
            "现象：OP02-095 攻击时效果抽1丢1，之后只能选择3张丢弃",
            "\t期望：正确处理丢1后，可以选择丢0");
        var input = CreateInput(category, description);
        var caseId = _operations.CreateCase(input);
        var reportPath = BugReportStore.SaveAtRoot(new
        {
            schema = "grandumi.feedback.report.v1",
            feedbackId = input.ExternalEventId,
            category,
            description,
        }, Path.Combine(_workspace, "reports"), input.ExternalEventId!, category);

        Assert.Equal(caseId, _operations.CreateCase(input));
        Assert.Single(_operations.ListCases(new OperationsCaseQuery()).Items);
        var saved = _operations.GetCase(caseId);
        Assert.Equal(description.Normalize(NormalizationForm.FormKC), saved.Description);
        Assert.Equal(category, saved.Summary.Category);
        Assert.Equal(input.ExternalEventId, saved.ExternalEventId);
        Assert.Single(saved.Evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(description, report.RootElement.GetProperty("description").GetString());
        Assert.Single(Directory.GetFiles(Path.Combine(_workspace, "reports"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void 四千字多行反馈可保存_超长反馈被拒绝且不产生额外记录()
    {
        var description = new string('甲', 1999) + "\n" + new string('乙', 2000);
        var input = CreateInput("bug", description);
        var caseId = _operations.CreateCase(input);
        Assert.Equal(description, _operations.GetCase(caseId).Description);
        var error = Assert.Throws<OperationsCenterException>(() =>
            _operations.CreateCase(CreateInput("bug", description + "丙")));
        Assert.Equal("invalid_request", error.Code);
        Assert.Single(_operations.ListCases(new OperationsCaseQuery()).Items);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\u001b")]
    [InlineData("\u000b")]
    [InlineData("\u000c")]
    [InlineData("\u007f")]
    [InlineData("\u0085")]
    public void 描述中的其他控制字符仍被拒绝_不产生半条反馈(string control)
    {
        var error = Assert.Throws<OperationsCenterException>(() =>
            _operations.CreateCase(CreateInput("bug", "问题描述" + control + "后续描述")));
        Assert.Equal("invalid_request", error.Code);
        Assert.Empty(_operations.ListCases(new OperationsCaseQuery()).Items);
    }

    [Fact]
    public void 放行描述换行不放宽标题账号和请求标识的格式校验()
    {
        var input = CreateInput("bug", "现象\n期望");
        Assert.Throws<OperationsCenterException>(() => _operations.CreateCase(input with { Title = "标题\n下一行" }));
        Assert.Throws<OperationsCenterException>(() => _operations.CreateCase(input with { ReporterAccount = "账号\n下一行" }));
        Assert.Throws<OperationsCenterException>(() => _operations.CreateCase(input with { RequestId = "request-\n非法标识" }));
        Assert.Empty(_operations.ListCases(new OperationsCaseQuery()).Items);
    }

    private static OperationsCaseCreate CreateInput(string category, string description)
    {
        var identity = FeedbackRequestIdentityFactory.Create("tester", "session-a", Guid.NewGuid().ToString("D"));
        return new OperationsCaseCreate(
            OperationsCaseSources.BugReport, category,
            category == "bug" ? "玩家 Bug 反馈" : "玩家优化建议", description,
            "tester", null, null, null, null, identity.FeedbackId, identity.SourceRequestId,
            [new OperationsCaseEvidenceInput("bug_report_evidence_v1", "{\"schema\":\"grandumi.feedback.evidence.v1\"}")],
            category == "bug" ? "high" : "normal");
    }

    public void Dispose()
    {
        _operations.Dispose();
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }
}
