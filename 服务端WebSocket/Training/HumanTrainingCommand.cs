using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandUMI.Training;

public static class HumanTrainingGateReasonCodes
{
    public const string NoMatchLogs = "no_match_logs";
    public const string NoReplayVerifiedLogs = "no_replay_verified_logs";
    public const string VerifiedDecisionEvidenceUnavailable = "verified_decision_evidence_unavailable";
}

/// <summary>
/// 真人训练发布前的机器可读门禁报告。它只陈述已经由独立历史 worker 证明的事实；
/// synthetic、fixture、握手成功或 preparation_ready 都不能增加真人证据计数。
/// </summary>
public sealed record HumanTrainingGateReport(
    string Schema,
    string Status,
    IReadOnlyList<string> ReasonCodes,
    string CoverageReportHash,
    string ArchiveCatalogHash,
    int TotalLogFiles,
    int ReplayVerifiedMatches,
    int VerifiedHumanDecisionMatches,
    int VerifiedHumanDecisionSamples,
    string RequiredDecisionEvidenceSchema,
    bool DatasetPublished,
    bool HumanModelTrained,
    bool ShadowEligible,
    bool ProductionEligible,
    bool ProductionRegistryModified,
    IReadOnlyDictionary<string, int> CoverageStatusCounts,
    string ReportHash);

public static class HumanTrainingGate
{
    public const string ReportSchema = "grandumi.human_training_gate_report.v1";
    public const string NoGoStatus = "no_go";
    public const string RequiredDecisionEvidenceSchema = "grandumi.verified_human_decision_evidence.v1";
    private const int MaximumReportBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static HumanTrainingGateReport Evaluate(ReplayCoverageReport coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ValidateCoverage(coverage);
        var replayVerified = coverage.Count(ReplayCoverageStatus.ReplayVerified);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        if (coverage.TotalFiles == 0) reasons.Add(HumanTrainingGateReasonCodes.NoMatchLogs);
        if (replayVerified == 0)
            reasons.Add(HumanTrainingGateReasonCodes.NoReplayVerifiedLogs);
        else
            // 当前 replay v2 响应只证明 checkpoint/终局一致，不承载动作前脱敏 observation 与
            // LegalActionSet。禁止在 controller 用当前 main 代替历史 artifact 补算这些 payload。
            reasons.Add(HumanTrainingGateReasonCodes.VerifiedDecisionEvidenceUnavailable);

        var statusCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in coverage.Summary.OrderBy(item => item.Status, StringComparer.Ordinal))
            statusCounts[item.Status] = item.Count;

        var withoutHash = new HumanTrainingGateReport(
            ReportSchema,
            NoGoStatus,
            Array.AsReadOnly(reasons.ToArray()),
            coverage.ReportHash,
            coverage.ArchiveCatalogHash,
            coverage.TotalFiles,
            replayVerified,
            VerifiedHumanDecisionMatches: 0,
            VerifiedHumanDecisionSamples: 0,
            RequiredDecisionEvidenceSchema,
            DatasetPublished: false,
            HumanModelTrained: false,
            ShadowEligible: false,
            ProductionEligible: false,
            ProductionRegistryModified: false,
            statusCounts,
            ReportHash: string.Empty);
        return withoutHash with
        {
            ReportHash = CanonicalJson.Hash(
                JsonSerializer.SerializeToElement(withoutHash, JsonOptions),
                "reportHash"),
        };
    }

    public static void WriteAtomic(HumanTrainingGateReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        Validate(report);
        var fullPath = ValidateOutputPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"无法解析真人训练门禁报告目录：{path}");
        Directory.CreateDirectory(directory);
        var next = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.next-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            var bytes = CanonicalJson.Encode(JsonSerializer.SerializeToElement(report, JsonOptions));
            using (var stream = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }
            File.Move(next, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(next)) File.Delete(next);
        }
    }

    public static HumanTrainingGateReport Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > MaximumReportBytes)
            throw new InvalidDataException("真人训练门禁报告为空或超过大小上限");
        var content = bytes.AsSpan();
        if (content[^1] == (byte)'\n') content = content[..^1];
        if (content.Length == 0 || content.Contains((byte)'\r'))
            throw new InvalidDataException("真人训练门禁报告只能使用规范 JSON 与单个 LF 结尾");
        using var document = JsonDocument.Parse(content.ToArray());
        var canonical = CanonicalJson.Encode(document.RootElement);
        if (!content.SequenceEqual(canonical))
            throw new InvalidDataException("真人训练门禁报告不是规范 JSON");
        var report = document.RootElement.Deserialize<HumanTrainingGateReport>(JsonOptions)
            ?? throw new InvalidDataException("真人训练门禁报告为空");
        Validate(report);
        return report;
    }

    public static void Validate(HumanTrainingGateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var expectedReasons = new SortedSet<string>(StringComparer.Ordinal);
        if (report.TotalLogFiles == 0)
            expectedReasons.Add(HumanTrainingGateReasonCodes.NoMatchLogs);
        if (report.ReplayVerifiedMatches == 0)
            expectedReasons.Add(HumanTrainingGateReasonCodes.NoReplayVerifiedLogs);
        else
            expectedReasons.Add(HumanTrainingGateReasonCodes.VerifiedDecisionEvidenceUnavailable);
        if (!string.Equals(report.Schema, ReportSchema, StringComparison.Ordinal)
            || !string.Equals(report.Status, NoGoStatus, StringComparison.Ordinal)
            || report.ReasonCodes is null
            || !report.ReasonCodes.SequenceEqual(expectedReasons, StringComparer.Ordinal)
            || !string.Equals(report.RequiredDecisionEvidenceSchema, RequiredDecisionEvidenceSchema, StringComparison.Ordinal)
            || report.TotalLogFiles < 0
            || report.ReplayVerifiedMatches < 0
            || report.ReplayVerifiedMatches > report.TotalLogFiles
            || report.VerifiedHumanDecisionMatches != 0
            || report.VerifiedHumanDecisionSamples != 0
            || report.DatasetPublished
            || report.HumanModelTrained
            || report.ShadowEligible
            || report.ProductionEligible
            || report.ProductionRegistryModified
            || report.CoverageStatusCounts is null
            || !IsSha256(report.CoverageReportHash)
            || !IsSha256(report.ArchiveCatalogHash)
            || !IsSha256(report.ReportHash))
            throw new InvalidDataException("真人训练门禁报告身份、No-Go 边界或计数无效");
        if (report.CoverageStatusCounts.Count != ReplayCoverageStatus.All.Count
            || ReplayCoverageStatus.All.Any(status => !report.CoverageStatusCounts.ContainsKey(status))
            || report.CoverageStatusCounts.Any(pair =>
                !ReplayCoverageStatus.All.Contains(pair.Key, StringComparer.Ordinal) || pair.Value < 0)
            || report.CoverageStatusCounts.GetValueOrDefault(ReplayCoverageStatus.ReplayVerified)
                != report.ReplayVerifiedMatches
            || report.CoverageStatusCounts.Sum(pair => (long)pair.Value) != report.TotalLogFiles)
            throw new InvalidDataException("真人训练门禁报告覆盖统计不一致");
        var actualHash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(report, JsonOptions),
            "reportHash");
        if (!string.Equals(actualHash, report.ReportHash, StringComparison.Ordinal))
            throw new InvalidDataException("真人训练门禁报告自哈希不一致");
    }

    private static void ValidateCoverage(ReplayCoverageReport coverage)
    {
        if (!string.Equals(coverage.Schema, ReplayCoverageAudit.ReportSchema, StringComparison.Ordinal)
            || coverage.TotalFiles < 0
            || coverage.Summary is null
            || coverage.WorkerArtifacts is null
            || coverage.Entries is null
            || coverage.Summary.Count != ReplayCoverageStatus.All.Count
            || !coverage.Summary.Select(item => item.Status)
                .SequenceEqual(ReplayCoverageStatus.All, StringComparer.Ordinal)
            || coverage.Summary.Any(item => item.Count < 0)
            || coverage.Summary.Sum(item => (long)item.Count) != coverage.TotalFiles
            || coverage.Entries.Count != coverage.TotalFiles
            || coverage.Entries.Any(entry =>
                !ReplayCoverageStatus.All.Contains(entry.Status, StringComparer.Ordinal))
            || coverage.Entries.Select(entry => entry.SourceId)
                .Distinct(StringComparer.Ordinal).Count() != coverage.TotalFiles
            || !IsSha256(coverage.ArchiveCatalogHash)
            || !IsSha256(coverage.ReportHash))
            throw new InvalidDataException("重放覆盖报告身份、状态集合或计数无效");

        foreach (var item in coverage.Summary)
        {
            var actual = coverage.Entries.Count(entry =>
                string.Equals(entry.Status, item.Status, StringComparison.Ordinal));
            if (actual != item.Count)
                throw new InvalidDataException("重放覆盖报告摘要与逐文件条目不一致");
        }

        var actualHash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(coverage, JsonOptions),
            "reportHash");
        if (!string.Equals(actualHash, coverage.ReportHash, StringComparison.Ordinal))
            throw new InvalidDataException("重放覆盖报告自哈希不一致");
    }

    private static string ValidateOutputPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return fullPath;
        var allowedRoot = Path.GetFullPath(@"E:\GrandUMI-Temp\")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Windows 真人训练验证输出必须位于 E:\\GrandUMI-Temp，实际为：{fullPath}");
        return fullPath;
    }

    private static bool IsSha256(string? value)
        => value is { Length: 71 }
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>真人数据发布入口。当前只生成 No-Go 证据，不生成空数据集或空模型。</summary>
public static class HumanTrainingCommand
{
    public const int NoGoExitCode = 3;

    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = Parse(args);
            if (!File.Exists(options.LogsPath) && !Directory.Exists(options.LogsPath))
                throw new ArgumentException($"真人训练日志路径不存在：{options.LogsPath}");
            if (!Directory.Exists(options.ArchiveRoot))
                throw new ArgumentException($"真人训练工件归档根目录不存在：{options.ArchiveRoot}");

            var archives = ReplayArtifactArchiveCatalog.Load(options.ArchiveRoot);
            var coverage = await ReplayCoverageAudit.GenerateAndExecuteAsync(
                options.LogsPath,
                archives,
                options.DotnetExecutable,
                options.ExecutionOptions,
                cancellationToken);
            var report = HumanTrainingGate.Evaluate(coverage);
            HumanTrainingGate.WriteAtomic(report, options.OutputPath);
            Console.Error.WriteLine(
                $"[真人训练] No-Go：日志 {report.TotalLogFiles}，replay_verified {report.ReplayVerifiedMatches}，" +
                $"逐决策真人证据 {report.VerifiedHumanDecisionSamples}；原因 {string.Join(',', report.ReasonCodes)}。" +
                "未生成数据集、模型，也未修改生产 registry。");
            return NoGoExitCode;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[真人训练] 操作已取消；未生成数据集或模型。");
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[真人训练] 参数错误：{ex.Message}");
            PrintUsage();
            return 2;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ReplayArtifactArchiveException)
        {
            Console.Error.WriteLine($"[真人训练] 失败：{ex.Message}；未生成数据集或模型。");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("所有参数必须使用 --名称 值 成对传入");
            var name = args[index][2..];
            if (!KnownOptions.Contains(name, StringComparer.Ordinal))
                throw new ArgumentException($"未知参数：--{name}");
            if (!values.TryAdd(name, args[index + 1]))
                throw new ArgumentException($"参数重复：--{name}");
        }

        var logs = Required(values, "logs");
        var archiveRoot = Required(values, "archive-root");
        var output = Required(values, "output");
        var defaults = ReplayCoverageExecutionOptions.Default;
        var execution = new ReplayCoverageExecutionOptions(
            PositiveInt(values, "max-concurrency", defaults.MaximumConcurrency),
            PositiveInt(values, "stable-timeout-ms", defaults.StableTimeoutMilliseconds),
            PositiveInt(values, "worker-timeout-ms", defaults.WorkerTimeoutMilliseconds),
            PositiveInt(values, "probe-timeout-ms", defaults.ProbeTimeoutMilliseconds));
        execution.Validate();
        return new Options(
            Path.GetFullPath(logs),
            Path.GetFullPath(archiveRoot),
            Path.GetFullPath(output),
            values.GetValueOrDefault("dotnet"),
            execution);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"缺少 --{name}");

    private static int PositiveInt(
        IReadOnlyDictionary<string, string> values,
        string name,
        int fallback)
        => !values.TryGetValue(name, out var raw)
            ? fallback
            : int.TryParse(raw, out var parsed) && parsed > 0
                ? parsed
                : throw new ArgumentException($"--{name} 必须是正整数");

    private static void PrintUsage()
        => Console.Error.WriteLine(
            "用法：GrandUMIServer --training-human --logs <JSONL文件或目录> --archive-root <不可变归档目录> " +
            "--output <E:\\GrandUMI-Temp 下的门禁报告.json> [--dotnet <dotnet路径>] " +
            "[--max-concurrency <1..8>] [--stable-timeout-ms <毫秒>] " +
            "[--worker-timeout-ms <毫秒>] [--probe-timeout-ms <毫秒>]");

    private static readonly string[] KnownOptions =
    [
        "logs", "archive-root", "output", "dotnet", "max-concurrency",
        "stable-timeout-ms", "worker-timeout-ms", "probe-timeout-ms",
    ];

    private sealed record Options(
        string LogsPath,
        string ArchiveRoot,
        string OutputPath,
        string? DotnetExecutable,
        ReplayCoverageExecutionOptions ExecutionOptions);
}
