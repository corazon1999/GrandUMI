using System.Text.Json;
using System.Text.Json.Nodes;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class HumanTrainingGateTests
{
    [Fact]
    public void 零ReplayVerified_明确NoGo且所有发布资格为False()
    {
        var coverage = Coverage(Array.Empty<ReplayCoverageEntry>());

        var report = HumanTrainingGate.Evaluate(coverage);

        Assert.Equal(HumanTrainingGate.NoGoStatus, report.Status);
        Assert.Contains(HumanTrainingGateReasonCodes.NoMatchLogs, report.ReasonCodes);
        Assert.Contains(HumanTrainingGateReasonCodes.NoReplayVerifiedLogs, report.ReasonCodes);
        Assert.Equal(0, report.ReplayVerifiedMatches);
        Assert.Equal(0, report.VerifiedHumanDecisionMatches);
        Assert.Equal(0, report.VerifiedHumanDecisionSamples);
        Assert.False(report.DatasetPublished);
        Assert.False(report.HumanModelTrained);
        Assert.False(report.ShadowEligible);
        Assert.False(report.ProductionEligible);
        Assert.False(report.ProductionRegistryModified);
        HumanTrainingGate.Validate(report);
    }

    [Fact]
    public void 只有ReplayVerified但无逐决策Evidence_仍然NoGo()
    {
        var entry = Entry(ReplayCoverageStatus.ReplayVerified, "sha256:" + new string('4', 64));
        var coverage = Coverage([entry]);

        var report = HumanTrainingGate.Evaluate(coverage);

        Assert.Equal(1, report.ReplayVerifiedMatches);
        Assert.DoesNotContain(HumanTrainingGateReasonCodes.NoReplayVerifiedLogs, report.ReasonCodes);
        Assert.Contains(HumanTrainingGateReasonCodes.VerifiedDecisionEvidenceUnavailable, report.ReasonCodes);
        Assert.Equal(0, report.VerifiedHumanDecisionSamples);
        Assert.False(report.DatasetPublished);
        Assert.False(report.HumanModelTrained);
    }

    [Fact]
    public void 门禁报告篡改后即使字段仍像NoGo_自哈希也拒绝()
    {
        var root = NewGrandUmiTempDirectory("human-gate-tamper");
        try
        {
            var path = Path.Combine(root, "gate.json");
            var report = HumanTrainingGate.Evaluate(Coverage(Array.Empty<ReplayCoverageEntry>()));
            HumanTrainingGate.WriteAtomic(report, path);
            var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            node["coverageReportHash"] = "sha256:" + new string('e', 64);
            File.WriteAllText(path, node.ToJsonString());

            var error = Assert.Throws<InvalidDataException>(() => HumanTrainingGate.Load(path));
            Assert.Contains("自哈希", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 门禁报告语义矛盾即使重算自哈希_仍然拒绝()
    {
        var report = HumanTrainingGate.Evaluate(Coverage(Array.Empty<ReplayCoverageEntry>()));
        var contradictory = Rehash(report with
        {
            ReasonCodes = Array.AsReadOnly(new[]
            {
                HumanTrainingGateReasonCodes.VerifiedDecisionEvidenceUnavailable,
            }),
            ReportHash = string.Empty,
        });

        var error = Assert.Throws<InvalidDataException>(() => HumanTrainingGate.Validate(contradictory));
        Assert.Contains("No-Go 边界", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 门禁报告未知字段即使保留原自哈希_仍然拒绝()
    {
        var root = NewGrandUmiTempDirectory("human-gate-unknown-field");
        try
        {
            var path = Path.Combine(root, "gate.json");
            var report = HumanTrainingGate.Evaluate(Coverage(Array.Empty<ReplayCoverageEntry>()));
            var node = JsonSerializer.SerializeToNode(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            })!.AsObject();
            node["unexpectedEvidence"] = false;
            WriteCanonical(path, node);

            Assert.Throws<JsonException>(() => HumanTrainingGate.Load(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 门禁报告缺少False字段即使可由默认值补齐_仍然拒绝()
    {
        var root = NewGrandUmiTempDirectory("human-gate-missing-field");
        try
        {
            var path = Path.Combine(root, "gate.json");
            var report = HumanTrainingGate.Evaluate(Coverage(Array.Empty<ReplayCoverageEntry>()));
            var node = JsonSerializer.SerializeToNode(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            })!.AsObject();
            Assert.True(node.Remove("datasetPublished"));
            WriteCanonical(path, node);

            Assert.Throws<JsonException>(() => HumanTrainingGate.Load(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 覆盖报告摘要与条目不一致_拒绝生成门禁报告()
    {
        var coverage = Coverage(Array.Empty<ReplayCoverageEntry>());
        var invalid = coverage with
        {
            TotalFiles = 1,
            Summary = Array.AsReadOnly(coverage.Summary.Select(item =>
                string.Equals(item.Status, ReplayCoverageStatus.MissingIdentity, StringComparison.Ordinal)
                    ? item with { Count = 1 }
                    : item).ToArray()),
            ReportHash = string.Empty,
        };
        invalid = invalid with
        {
            ReportHash = CanonicalJson.Hash(
                JsonSerializer.SerializeToElement(invalid, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }),
                "reportHash"),
        };

        var error = Assert.Throws<InvalidDataException>(() => HumanTrainingGate.Evaluate(invalid));
        Assert.Contains("计数无效", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 真人CLI无日志_退出NoGo且只写门禁报告不写样本或模型()
    {
        var root = NewGrandUmiTempDirectory("human-cli-no-go");
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
            var archives = Directory.CreateDirectory(Path.Combine(root, "archives")).FullName;
            var output = Path.Combine(root, "human-training-gate.v1.json");

            var exitCode = await HumanTrainingCommand.RunAsync(
            [
                "--logs", logs,
                "--archive-root", archives,
                "--output", output,
            ]);

            Assert.Equal(HumanTrainingCommand.NoGoExitCode, exitCode);
            var report = HumanTrainingGate.Load(output);
            Assert.Equal(0, report.ReplayVerifiedMatches);
            Assert.Contains(HumanTrainingGateReasonCodes.NoReplayVerifiedLogs, report.ReasonCodes);
            Assert.False(File.Exists(Path.Combine(root, "samples.jsonl")));
            Assert.False(File.Exists(Path.Combine(root, "dataset-manifest.json")));
            Assert.False(File.Exists(Path.Combine(root, "model-manifest.json")));
            var content = File.ReadAllBytes(output);
            Assert.Equal((byte)'\n', content[^1]);
            Assert.DoesNotContain((byte)'\r', content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Windows输出不在E盘_拒绝且不落盘()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = NewGrandUmiTempDirectory("human-cli-output-policy");
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
            var archives = Directory.CreateDirectory(Path.Combine(root, "archives")).FullName;
            var forbidden = @"C:\human-training-gate-should-not-exist.json";

            var exitCode = await HumanTrainingCommand.RunAsync(
            [
                "--logs", logs,
                "--archive-root", archives,
                "--output", forbidden,
            ]);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(forbidden));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ReplayCoverageReport Coverage(IReadOnlyList<ReplayCoverageEntry> entries)
    {
        var summaries = ReplayCoverageStatus.All.Select(status =>
            new ReplayCoverageSummary(status, entries.Count(entry =>
                string.Equals(entry.Status, status, StringComparison.Ordinal)))).ToArray();
        var withoutHash = new ReplayCoverageReport(
            ReplayCoverageAudit.ReportSchema,
            "sha256:" + new string('a', 64),
            entries.Count,
            Array.AsReadOnly(summaries),
            Array.Empty<ReplayCoverageWorkerArtifact>(),
            entries,
            ReportHash: string.Empty);
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }),
            "reportHash");
        return withoutHash with { ReportHash = hash };
    }

    private static ReplayCoverageEntry Entry(string status, string replayDigest)
        => new(
            "match.jsonl",
            "sha256:" + new string('b', 64),
            "m1",
            "grandumi-runtime-" + new string('c', 64),
            status,
            "dispatcher_replay_verified",
            "artifact_replay_dispatch",
            replayDigest,
            "sha256:" + new string('d', 64));

    private static HumanTrainingGateReport Rehash(HumanTrainingGateReport report)
        => report with
        {
            ReportHash = CanonicalJson.Hash(
                JsonSerializer.SerializeToElement(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }),
                "reportHash"),
        };

    private static void WriteCanonical(string path, JsonNode node)
    {
        var bytes = CanonicalJson.Encode(JsonSerializer.SerializeToElement(node));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
    }

    private static string NewGrandUmiTempDirectory(string purpose)
    {
        var tempRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        var root = !string.IsNullOrWhiteSpace(tempRoot)
            ? tempRoot
            : Path.Combine(Path.GetTempPath(), "GrandUMI-Temp", "server-tests");
        var path = Path.Combine(root, purpose, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
