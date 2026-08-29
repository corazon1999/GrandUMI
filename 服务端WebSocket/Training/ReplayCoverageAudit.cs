using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrandUMI.Training;

public static class ReplayCoverageStatus
{
    public const string Legacy = "legacy";
    public const string MissingIdentity = "missing_identity";
    public const string MissingCheckpoint = "missing_checkpoint";
    public const string IdentityMismatch = "identity_mismatch";
    public const string ArtifactNotArchived = "artifact_not_archived";
    public const string PreparationReady = "preparation_ready";
    public const string ReplayVerified = "replay_verified";
    public const string ReplayDiverged = "replay_diverged";
    public const string ReplayTimeout = "replay_timeout";
    public const string ReplayWorkerFailed = "replay_worker_failed";
    public const string InvalidLog = "invalid_log";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Legacy,
        MissingIdentity,
        MissingCheckpoint,
        IdentityMismatch,
        ArtifactNotArchived,
        PreparationReady,
        ReplayVerified,
        ReplayDiverged,
        ReplayTimeout,
        ReplayWorkerFailed,
        InvalidLog,
    });
}

public sealed record ReplayCoverageEntry(
    string SourceId,
    string SourceFileHash,
    string? MatchId,
    string? ArtifactId,
    string Status,
    string ReasonCode,
    string Stage,
    string? ReplayDigest,
    string StableHash);

public sealed record ReplayCoverageSummary(string Status, int Count);

public sealed record ReplayCoverageWorkerArtifact(
    string ArtifactId,
    bool EntrypointAvailable,
    bool HandshakeVerified,
    string ReasonCode,
    string StableHash);

public sealed record ReplayCoverageReport(
    string Schema,
    string ArchiveCatalogHash,
    int TotalFiles,
    IReadOnlyList<ReplayCoverageSummary> Summary,
    IReadOnlyList<ReplayCoverageWorkerArtifact> WorkerArtifacts,
    IReadOnlyList<ReplayCoverageEntry> Entries,
    string ReportHash)
{
    public int Count(string status)
        => Summary.Single(item => string.Equals(item.Status, status, StringComparison.Ordinal)).Count;
}

public sealed record ReplayTestCandidateArtifact(
    string ArtifactId,
    string EngineCommit,
    string ManifestHash,
    string ArchiveDirectoryName,
    ReplayRuntimeIdentity RuntimeIdentity,
    bool ReplayWorkerDeclaredAvailable,
    bool ReplayWorkerAvailable,
    string ReplayWorkerAvailabilityReason,
    bool ProductionRegistryEligible,
    string Reason);

public sealed record ReplayTestCandidateCatalog(
    string Schema,
    string Environment,
    string ProductionRegistry,
    bool ProductionRegistryModified,
    string ArchiveCatalogHash,
    IReadOnlyList<ReplayTestCandidateArtifact> Artifacts,
    string CatalogHash);

public sealed record ReplayCoverageExecutionOptions(
    int MaximumConcurrency,
    int StableTimeoutMilliseconds,
    int WorkerTimeoutMilliseconds,
    int ProbeTimeoutMilliseconds)
{
    public static ReplayCoverageExecutionOptions Default { get; } = new(
        MaximumConcurrency: 2,
        StableTimeoutMilliseconds: 15_000,
        WorkerTimeoutMilliseconds: 120_000,
        ProbeTimeoutMilliseconds: 45_000);

    internal void Validate()
    {
        if (MaximumConcurrency is <= 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrency), "批量并发必须在 1..8。 ");
        if (StableTimeoutMilliseconds is <= 0 or > 120_000)
            throw new ArgumentOutOfRangeException(nameof(StableTimeoutMilliseconds));
        if (WorkerTimeoutMilliseconds is <= 0 or > 15 * 60_000)
            throw new ArgumentOutOfRangeException(nameof(WorkerTimeoutMilliseconds));
        if (ProbeTimeoutMilliseconds is <= 0 or > 5 * 60_000)
            throw new ArgumentOutOfRangeException(nameof(ProbeTimeoutMilliseconds));
    }
}

/// <summary>只接纳完整验证通过的最终目录；.staging 永远不参与取回或覆盖统计。</summary>
public sealed class ReplayArtifactArchiveCatalog
{
    private ReplayArtifactArchiveCatalog(
        string archiveRoot,
        IReadOnlyList<VerifiedReplayArtifactArchive> archives,
        string catalogHash,
        ReplayArtifactRegistry preparationRegistry)
    {
        ArchiveRoot = archiveRoot;
        Archives = archives;
        CatalogHash = catalogHash;
        PreparationRegistry = preparationRegistry;
        ByArtifactId = archives.ToDictionary(
            archive => archive.Manifest.ArtifactId,
            StringComparer.Ordinal);
    }

    public string ArchiveRoot { get; }
    public IReadOnlyList<VerifiedReplayArtifactArchive> Archives { get; }
    public string CatalogHash { get; }
    public ReplayArtifactRegistry PreparationRegistry { get; }
    public IReadOnlyDictionary<string, VerifiedReplayArtifactArchive> ByArtifactId { get; }

    public static ReplayArtifactArchiveCatalog Load(string archiveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archiveRoot));
        if (!Directory.Exists(root))
            throw new ReplayArtifactArchiveException($"归档 catalog 根目录不存在：{root}");
        RejectLinkedAncestors(root, "归档 catalog 根目录");
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0 || rootInfo.LinkTarget is not null)
            throw new ReplayArtifactArchiveException("归档 catalog 根目录不能是符号链接或重解析点。");

        var archives = new List<VerifiedReplayArtifactArchive>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, ".staging", StringComparison.Ordinal))
            {
                var stagingInfo = new DirectoryInfo(entry);
                if (!stagingInfo.Exists
                    || (stagingInfo.Attributes & FileAttributes.ReparsePoint) != 0
                    || stagingInfo.LinkTarget is not null)
                    throw new ReplayArtifactArchiveException("归档 .staging 必须是普通目录。");
                continue;
            }
            if (!Directory.Exists(entry))
                throw new ReplayArtifactArchiveException($"归档根目录包含未知文件：{name}");
            archives.Add(ReplayArtifactArchive.Verify(entry));
        }

        var ordered = archives
            .OrderBy(archive => archive.Manifest.ArtifactId, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered
            .GroupBy(archive => archive.Manifest.ArtifactId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ReplayArtifactArchiveException($"归档 catalog 包含重复 artifactId：{duplicate.Key}");

        var catalogSeed = JsonSerializer.SerializeToElement(new
        {
            schema = "grandumi.test_replay_artifact_catalog_seed.v1",
            artifacts = ordered.Select(archive => new
            {
                archive.Manifest.ArtifactId,
                archive.Manifest.ManifestHash,
            }).ToArray(),
        });
        var catalogHash = CanonicalJson.Hash(catalogSeed);
        var registry = BuildPreparationRegistry(ordered, catalogHash);
        return new ReplayArtifactArchiveCatalog(
            root,
            Array.AsReadOnly(ordered),
            catalogHash,
            registry);
    }

    private static void RejectLinkedAncestors(string path, string context)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        if (!current.Exists) current = current.Parent;
        while (current is not null)
        {
            if (current.Exists
                && ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null))
                throw new ReplayArtifactArchiveException(
                    $"{context}经过符号链接或重解析点：{current.FullName}");
            current = current.Parent;
        }
    }

    private static ReplayArtifactRegistry BuildPreparationRegistry(
        IReadOnlyList<VerifiedReplayArtifactArchive> archives,
        string catalogHash)
    {
        var root = JsonSerializer.SerializeToNode(new
        {
            schema = ReplayArtifactRegistry.Schema,
            registryVersion = "test-archive-catalog-" + catalogHash["sha256:".Length..],
            artifacts = archives.Select(archive =>
            {
                var descriptor = ReplayArtifactArchive.CreateTestDescriptor(archive.Manifest);
                return new
                {
                    matchLogSchema = descriptor.MatchLogSchema,
                    eventAdapterVersion = descriptor.EventAdapterVersion,
                    engineArtifactId = descriptor.EngineArtifactId,
                    engineCommit = descriptor.EngineCommit,
                    binarySha256 = descriptor.BinarySha256,
                    rulesVersion = descriptor.RulesVersion,
                    rulesetManifestHash = descriptor.RulesetManifestHash,
                    cardDbContentHash = descriptor.CardDbContentHash,
                    rngAlgorithmVersion = descriptor.RngAlgorithmVersion,
                    deterministicIdVersion = descriptor.DeterministicIdVersion,
                    openingProtocolVersion = descriptor.OpeningProtocolVersion,
                    replayConfigSchema = descriptor.ReplayConfigSchema,
                    executable = descriptor.Executable,
                };
            }).ToArray(),
        })!.AsObject();
        root["registryHash"] = CanonicalJson.Hash(JsonSerializer.SerializeToElement(root));
        return ReplayArtifactRegistry.Parse(root.ToJsonString());
    }
}

/// <summary>稳定、无时间戳的 JSON/Markdown 对局覆盖审计。</summary>
public static class ReplayCoverageAudit
{
    public const string ReportSchema = "grandumi.replay_coverage_report.v2";
    public const string CandidateCatalogSchema = "grandumi.test_replay_artifact_candidates.v2";
    private const long MaximumLogBytes = 64L * 1024 * 1024;

    public static ReplayCoverageReport Generate(
        string matchLogPath,
        ReplayArtifactArchiveCatalog archives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchLogPath);
        ArgumentNullException.ThrowIfNull(archives);
        var sources = EnumerateLogSources(matchLogPath);
        var entries = sources
            .Select(source => Classify(source.FullPath, source.SourceId, archives))
            .OrderBy(entry => entry.SourceId, StringComparer.Ordinal)
            .ToArray();
        var workers = archives.Archives
            .Select(archive => WorkerArtifact(
                archive.Manifest.ArtifactId,
                archive.Manifest.ReplayWorkerEntrypoint.Available,
                handshakeVerified: false,
                archive.Manifest.ReplayWorkerEntrypoint.Available
                    ? "worker_not_probed"
                    : ReplayQuarantineCodes.WorkerNotRegistered))
            .OrderBy(worker => worker.ArtifactId, StringComparer.Ordinal)
            .ToArray();
        return BuildReport(archives.CatalogHash, entries, workers);
    }

    public static async Task<ReplayCoverageReport> GenerateAndExecuteAsync(
        string matchLogPath,
        ReplayArtifactArchiveCatalog archives,
        string? trustedDotnetExecutable = null,
        ReplayCoverageExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchLogPath);
        ArgumentNullException.ThrowIfNull(archives);
        options ??= ReplayCoverageExecutionOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var baseline = Generate(matchLogPath, archives);
        var processWorkers = new Dictionary<string, ProcessArtifactReplayWorker>(StringComparer.Ordinal);
        var workerArtifacts = new List<ReplayCoverageWorkerArtifact>(archives.Archives.Count);
        foreach (var archive in archives.Archives.OrderBy(
                     item => item.Manifest.ArtifactId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!archive.Manifest.ReplayWorkerEntrypoint.Available)
            {
                workerArtifacts.Add(WorkerArtifact(
                    archive.Manifest.ArtifactId,
                    entrypointAvailable: false,
                    handshakeVerified: false,
                    ReplayQuarantineCodes.WorkerNotRegistered));
                continue;
            }

            try
            {
                var worker = new ProcessArtifactReplayWorker(
                    archive,
                    trustedDotnetExecutable,
                    TimeSpan.FromMilliseconds(options.WorkerTimeoutMilliseconds));
                _ = await worker.ProbeAsync(
                    TimeSpan.FromMilliseconds(options.ProbeTimeoutMilliseconds),
                    cancellationToken);
                processWorkers.Add(archive.Manifest.ArtifactId, worker);
                workerArtifacts.Add(WorkerArtifact(
                    archive.Manifest.ArtifactId,
                    entrypointAvailable: true,
                    handshakeVerified: true,
                    "worker_handshake_verified"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is ArtifactReplayProcessException
                or ReplayArtifactArchiveException
                or IOException
                or UnauthorizedAccessException)
            {
                throw new ReplayArtifactArchiveException(
                    $"归档声明 worker 可用但历史 DLL 自检/握手失败：{archive.Manifest.ArtifactId}；{ex.Message}",
                    ex);
            }
        }

        if (baseline.Entries.All(entry =>
                !string.Equals(entry.Status, ReplayCoverageStatus.PreparationReady, StringComparison.Ordinal)))
            return BuildReport(archives.CatalogHash, baseline.Entries, workerArtifacts);

        var refreshedSources = EnumerateLogSources(matchLogPath);
        var baselineSourceIds = baseline.Entries
            .Select(entry => entry.SourceId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var refreshedSourceIds = refreshedSources
            .Select(source => source.SourceId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!baselineSourceIds.SequenceEqual(refreshedSourceIds, StringComparer.Ordinal))
            throw new ReplayArtifactArchiveException(
                "批量执行期间日志文件集合发生变化，拒绝生成跨两个目录快照的部分报告。");
        var sources = refreshedSources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);
        var dispatcher = new ArtifactReplayWorkerDispatcher(processWorkers.Values);
        using var semaphore = new SemaphoreSlim(options.MaximumConcurrency, options.MaximumConcurrency);
        using var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task<ReplayCoverageEntry> RunEntryAsync(ReplayCoverageEntry entry)
        {
            try
            {
                return await ExecutePreparedEntryAsync(
                    entry,
                    sources,
                    archives,
                    processWorkers,
                    dispatcher,
                    semaphore,
                    options,
                    batchCancellation.Token);
            }
            catch
            {
                try { batchCancellation.Cancel(); } catch (ObjectDisposedException) { }
                throw;
            }
        }

        var entries = await Task.WhenAll(baseline.Entries.Select(RunEntryAsync));
        cancellationToken.ThrowIfCancellationRequested();
        return BuildReport(archives.CatalogHash, entries, workerArtifacts);
    }

    private static async Task<ReplayCoverageEntry> ExecutePreparedEntryAsync(
        ReplayCoverageEntry entry,
        IReadOnlyDictionary<string, LogSource> sources,
        ReplayArtifactArchiveCatalog archives,
        IReadOnlyDictionary<string, ProcessArtifactReplayWorker> processWorkers,
        ArtifactReplayWorkerDispatcher dispatcher,
        SemaphoreSlim semaphore,
        ReplayCoverageExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(entry.Status, ReplayCoverageStatus.PreparationReady, StringComparison.Ordinal))
            return entry;
        if (entry.ArtifactId is null
            || !archives.ByArtifactId.TryGetValue(entry.ArtifactId, out var archive))
            throw new ReplayArtifactArchiveException("准备层条目丢失对应归档，拒绝生成部分报告。");
        if (!archive.Manifest.ReplayWorkerEntrypoint.Available)
            return entry;
        if (!processWorkers.ContainsKey(entry.ArtifactId))
            throw new ReplayArtifactArchiveException("已握手 worker 未进入受控 dispatcher。");
        if (!sources.TryGetValue(entry.SourceId, out var source))
            throw new ReplayArtifactArchiveException("批量执行期间日志源消失，拒绝生成部分报告。");

        var bytes = await File.ReadAllBytesAsync(source.FullPath, cancellationToken);
        if (!string.Equals(CanonicalJson.Sha256(bytes), entry.SourceFileHash, StringComparison.Ordinal))
            throw new ReplayArtifactArchiveException("批量执行期间日志字节发生变化，拒绝混合两个快照。");
        var preparation = ReplayMatchPreparation.Prepare(
            bytes,
            entry.SourceId,
            archives.PreparationRegistry);
        var prepared = preparation.Prepared
            ?? throw new ReplayArtifactArchiveException(
                "批量执行期间准备结果改变，拒绝生成部分报告。");

        await semaphore.WaitAsync(cancellationToken);
        ArtifactReplayExecutionResult result;
        try
        {
            result = await dispatcher.ExecuteAsync(
                prepared,
                options.StableTimeoutMilliseconds,
                options.WorkerTimeoutMilliseconds,
                cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (result.Verified is { } verified)
            return Entry(
                entry.SourceId,
                entry.SourceFileHash,
                entry.MatchId,
                entry.ArtifactId,
                ReplayCoverageStatus.ReplayVerified,
                "dispatcher_replay_verified",
                "artifact_replay_dispatch",
                verified.ReplayDigest);

        var quarantine = result.Quarantine
            ?? throw new ReplayArtifactArchiveException("dispatcher 没有返回 verified 或 quarantine。");
        if (IsSystemicProtocolFailure(quarantine.ReasonCode))
            throw new ReplayArtifactArchiveException(
                $"归档 worker 出现系统性协议错误：{entry.ArtifactId}；" +
                $"{quarantine.ReasonCode}/{quarantine.Stage}；{quarantine.Message}");
        var status = IsReplayDivergence(quarantine.ReasonCode)
            ? ReplayCoverageStatus.ReplayDiverged
            : string.Equals(quarantine.ReasonCode, ReplayQuarantineCodes.WorkerTimeout, StringComparison.Ordinal)
                || string.Equals(quarantine.ReasonCode, ReplayQuarantineCodes.StableWaitTimeout, StringComparison.Ordinal)
                ? ReplayCoverageStatus.ReplayTimeout
                : ReplayCoverageStatus.ReplayWorkerFailed;
        return Entry(
            entry.SourceId,
            entry.SourceFileHash,
            entry.MatchId,
            entry.ArtifactId,
            status,
            quarantine.ReasonCode,
            quarantine.Stage);
    }

    private static bool IsSystemicProtocolFailure(string reasonCode)
        => string.Equals(reasonCode, ReplayQuarantineCodes.WorkerProtocolMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.WorkerArtifactMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.WorkerTerminationFailed, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.WorkerNotRegistered, StringComparison.Ordinal);

    private static bool IsReplayDivergence(string reasonCode)
        => string.Equals(reasonCode, ReplayQuarantineCodes.StateCheckpointMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.PublicCheckpointMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.RandomTraceMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.TerminalMismatch, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.ReplayActionRejected, StringComparison.Ordinal)
            || string.Equals(reasonCode, ReplayQuarantineCodes.TapeContinuesAfterGameOver, StringComparison.Ordinal);

    private static ReplayCoverageReport BuildReport(
        string archiveCatalogHash,
        IReadOnlyList<ReplayCoverageEntry> entries,
        IReadOnlyList<ReplayCoverageWorkerArtifact> workers)
    {
        var orderedEntries = entries.OrderBy(entry => entry.SourceId, StringComparer.Ordinal).ToArray();
        var summary = ReplayCoverageStatus.All
            .Select(status => new ReplayCoverageSummary(
                status,
                orderedEntries.Count(entry => string.Equals(entry.Status, status, StringComparison.Ordinal))))
            .ToArray();
        var withoutHash = new ReplayCoverageReport(
            ReportSchema,
            archiveCatalogHash,
            orderedEntries.Length,
            Array.AsReadOnly(summary),
            Array.AsReadOnly(workers.OrderBy(worker => worker.ArtifactId, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(orderedEntries),
            ReportHash: string.Empty);
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }),
            "reportHash");
        return withoutHash with { ReportHash = hash };
    }

    public static void WriteOutputs(
        ReplayCoverageReport report,
        ReplayArtifactArchiveCatalog archives,
        string jsonPath,
        string markdownPath,
        string candidateCatalogPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(archives);
        EnsureOutputOutsideArchive(archives.ArchiveRoot, jsonPath);
        EnsureOutputOutsideArchive(archives.ArchiveRoot, markdownPath);
        EnsureOutputOutsideArchive(archives.ArchiveRoot, candidateCatalogPath);
        EnsureDistinctOutputs(jsonPath, markdownPath, candidateCatalogPath);

        var json = ReplayArtifactArchive.SerializeCanonical(report) + "\n";
        var markdown = BuildMarkdown(report);
        var candidate = BuildCandidateCatalog(archives, report.WorkerArtifacts);
        var candidateJson = ReplayArtifactArchive.SerializeCanonical(candidate) + "\n";
        WriteAtomic(jsonPath, Encoding.UTF8.GetBytes(json));
        WriteAtomic(markdownPath, Encoding.UTF8.GetBytes(markdown));
        WriteAtomic(candidateCatalogPath, Encoding.UTF8.GetBytes(candidateJson));
    }

    public static ReplayTestCandidateCatalog BuildCandidateCatalog(
        ReplayArtifactArchiveCatalog archives,
        IReadOnlyList<ReplayCoverageWorkerArtifact>? workerArtifacts = null)
    {
        var workerByArtifactId = (workerArtifacts ?? Array.Empty<ReplayCoverageWorkerArtifact>())
            .ToDictionary(worker => worker.ArtifactId, StringComparer.Ordinal);
        var artifacts = archives.Archives
            .Select(archive =>
            {
                workerByArtifactId.TryGetValue(archive.Manifest.ArtifactId, out var worker);
                return new ReplayTestCandidateArtifact(
                    archive.Manifest.ArtifactId,
                    archive.Manifest.EngineCommit,
                    archive.Manifest.ManifestHash,
                    Path.GetFileName(archive.ArchiveDirectory),
                    archive.Manifest.RuntimeIdentity,
                    archive.Manifest.ReplayWorkerEntrypoint.Available,
                    worker?.HandshakeVerified == true,
                    worker?.ReasonCode ?? "worker_not_probed",
                    archive.Manifest.CandidateStatus.ProductionRegistryEligible,
                    archive.Manifest.CandidateStatus.Reason);
            })
            .OrderBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .ToArray();
        var withoutHash = new ReplayTestCandidateCatalog(
            CandidateCatalogSchema,
            "test",
            "服务端WebSocket/Training/Artifacts/replay-artifact-registry.v1.json",
            ProductionRegistryModified: false,
            archives.CatalogHash,
            Array.AsReadOnly(artifacts),
            CatalogHash: string.Empty);
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }),
            "catalogHash");
        return withoutHash with { CatalogHash = hash };
    }

    public static string BuildMarkdown(ReplayCoverageReport report)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReplayCoverageStatus.Legacy] = "Legacy adapter",
            [ReplayCoverageStatus.MissingIdentity] = "缺精确运行身份",
            [ReplayCoverageStatus.MissingCheckpoint] = "缺 checkpoint 契约",
            [ReplayCoverageStatus.IdentityMismatch] = "身份与归档不匹配",
            [ReplayCoverageStatus.ArtifactNotArchived] = "artifact 未归档",
            [ReplayCoverageStatus.PreparationReady] = "仅准备层就绪",
            [ReplayCoverageStatus.ReplayVerified] = "真实重放验证通过",
            [ReplayCoverageStatus.ReplayDiverged] = "重放 checkpoint/终局分歧",
            [ReplayCoverageStatus.ReplayTimeout] = "重放超时",
            [ReplayCoverageStatus.ReplayWorkerFailed] = "独立 worker 失败",
            [ReplayCoverageStatus.InvalidLog] = "其他无效日志",
        };
        var builder = new StringBuilder();
        builder.AppendLine("# 测试服重放覆盖审计");
        builder.AppendLine();
        builder.AppendLine($"- 报告哈希：`{report.ReportHash}`");
        builder.AppendLine($"- 归档目录集哈希：`{report.ArchiveCatalogHash}`");
        builder.AppendLine($"- 扫描 JSONL：{report.TotalFiles}");
        builder.AppendLine();
        builder.AppendLine("| 状态 | 稳定代码 | 数量 |");
        builder.AppendLine("|---|---|---:|");
        foreach (var item in report.Summary)
            builder.AppendLine($"| {labels[item.Status]} | `{item.Status}` | {item.Count} |");
        builder.AppendLine();
        builder.AppendLine(
            "> 只有历史 DLL 握手成功、该局 dispatcher 完整执行并通过全部 checkpoint/终局及响应 lineage 复核，才计为 `replay_verified`。准备层、worker 可用性和真实重放证据互不替代。");
        builder.AppendLine();
        builder.AppendLine("## 归档 worker 可用性");
        builder.AppendLine();
        builder.AppendLine("| artifactId | 入口声明 | 历史 DLL 握手 | 原因 |");
        builder.AppendLine("|---|---:|---:|---|");
        if (report.WorkerArtifacts.Count == 0)
        {
            builder.AppendLine("| _无归档_ |  |  |  |");
        }
        else
        {
            foreach (var worker in report.WorkerArtifacts)
                builder.AppendLine(
                    $"| {EscapeMarkdown(worker.ArtifactId)} | {(worker.EntrypointAvailable ? "是" : "否")} | " +
                    $"{(worker.HandshakeVerified ? "是" : "否")} | `{worker.ReasonCode}` |");
        }
        builder.AppendLine();
        builder.AppendLine("## 明细");
        builder.AppendLine();
        builder.AppendLine("| 日志 | matchId | artifactId | 状态 | 原因 |");
        builder.AppendLine("|---|---|---|---|---|");
        if (report.Entries.Count == 0)
        {
            builder.AppendLine("| _无日志_ |  |  |  |  |");
        }
        else
        {
            foreach (var entry in report.Entries)
            {
                builder.AppendLine(
                    $"| {EscapeMarkdown(entry.SourceId)} | {EscapeMarkdown(entry.MatchId ?? string.Empty)} | " +
                    $"{EscapeMarkdown(entry.ArtifactId ?? string.Empty)} | `{entry.Status}` | `{entry.ReasonCode}` / `{entry.Stage}` |");
            }
        }
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static ReplayCoverageEntry Classify(
        string fullPath,
        string sourceId,
        ReplayArtifactArchiveCatalog archives)
    {
        var info = new FileInfo(fullPath);
        if (info.Length > MaximumLogBytes)
        {
            var streamHash = ReplayContentManifest.HashFile(fullPath);
            return Entry(
                sourceId,
                streamHash,
                matchId: null,
                artifactId: null,
                ReplayCoverageStatus.InvalidLog,
                "log_too_large",
                "coverage_audit");
        }

        var bytes = File.ReadAllBytes(fullPath);
        var sourceHash = CanonicalJson.Sha256(bytes);
        AdaptedMatchLog log;
        try
        {
            log = MatchLogEventAdapter.Adapt(bytes, sourceId);
        }
        catch (ReplayQuarantineException ex)
        {
            var status = string.Equals(
                ex.ReasonCode,
                ReplayQuarantineCodes.MissingVersionIdentity,
                StringComparison.Ordinal)
                ? ReplayCoverageStatus.MissingIdentity
                : ReplayCoverageStatus.InvalidLog;
            return Entry(
                sourceId,
                sourceHash,
                ex.MatchId,
                artifactId: null,
                status,
                ex.ReasonCode,
                ex.Stage);
        }

        var identity = log.Header.VersionIdentity;
        if (string.Equals(identity.EventAdapterVersion, MatchLogEventAdapter.LegacyAdapterVersion, StringComparison.Ordinal))
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.Legacy,
                "legacy_event_adapter",
                "coverage_audit");

        var matchStartPayload = log.Events[0].Payload;
        if (!matchStartPayload.TryGetProperty("replayRuntimeManifestHash", out var runtimeHashElement)
            || runtimeHashElement.ValueKind != JsonValueKind.String
            || runtimeHashElement.GetString() is not { Length: > 0 } runtimeManifestHash)
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.MissingIdentity,
                "missing_runtime_manifest_hash",
                "coverage_audit");

        try
        {
            ReplayRuntimeIdentityFactory.ValidateIdentity(new ReplayRuntimeIdentity(
                identity.MatchLogSchema,
                identity.EventAdapterVersion,
                identity.EngineArtifactId,
                identity.EngineCommit,
                identity.BinarySha256,
                identity.RulesVersion,
                identity.RulesetManifestHash,
                identity.CardDbContentHash,
                identity.RngAlgorithmVersion,
                identity.DeterministicIdVersion,
                identity.OpeningProtocolVersion,
                identity.ReplayConfigSchema,
                runtimeManifestHash));
        }
        catch (InvalidOperationException)
        {
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.IdentityMismatch,
                ReplayQuarantineCodes.ArtifactIdentityMismatch,
                "runtime_identity");
        }

        if (!archives.ByArtifactId.TryGetValue(identity.EngineArtifactId, out var archive))
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.ArtifactNotArchived,
                ReplayQuarantineCodes.UnsupportedArtifact,
                "artifact_archive");

        if (!IdentityMatches(identity, runtimeManifestHash, archive.Manifest.RuntimeIdentity))
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.IdentityMismatch,
                ReplayQuarantineCodes.ArtifactIdentityMismatch,
                "artifact_archive");

        var preparation = ReplayMatchPreparation.Prepare(bytes, sourceId, archives.PreparationRegistry);
        if (!preparation.IsPrepared)
        {
            var quarantine = preparation.Quarantine!;
            var status = string.Equals(
                quarantine.ReasonCode,
                ReplayQuarantineCodes.ArtifactIdentityMismatch,
                StringComparison.Ordinal)
                ? ReplayCoverageStatus.IdentityMismatch
                : ReplayCoverageStatus.InvalidLog;
            return Entry(
                sourceId,
                sourceHash,
                quarantine.MatchId ?? log.Header.MatchId,
                identity.EngineArtifactId,
                status,
                quarantine.ReasonCode,
                quarantine.Stage);
        }

        var prepared = preparation.Prepared!;
        if (prepared.CheckpointContract is null)
            return Entry(
                sourceId,
                sourceHash,
                log.Header.MatchId,
                identity.EngineArtifactId,
                ReplayCoverageStatus.MissingCheckpoint,
                ReplayQuarantineCodes.MissingCheckpointContract,
                "checkpoint_contract");

        return Entry(
            sourceId,
            sourceHash,
            log.Header.MatchId,
            identity.EngineArtifactId,
            ReplayCoverageStatus.PreparationReady,
            archive.Manifest.ReplayWorkerEntrypoint.Available
                ? "batch_replay_not_executed"
                : ReplayQuarantineCodes.WorkerNotRegistered,
            "artifact_archive");
    }

    private static bool IdentityMatches(
        ReplayVersionIdentity log,
        string runtimeManifestHash,
        ReplayRuntimeIdentity archive)
        => string.Equals(log.MatchLogSchema, archive.MatchLogSchema, StringComparison.Ordinal)
            && string.Equals(log.EventAdapterVersion, archive.EventAdapterVersion, StringComparison.Ordinal)
            && string.Equals(log.EngineArtifactId, archive.EngineArtifactId, StringComparison.Ordinal)
            && string.Equals(log.EngineCommit, archive.EngineCommit, StringComparison.Ordinal)
            && string.Equals(log.BinarySha256, archive.BinarySha256, StringComparison.Ordinal)
            && string.Equals(log.RulesVersion, archive.RulesVersion, StringComparison.Ordinal)
            && string.Equals(log.RulesetManifestHash, archive.RulesetManifestHash, StringComparison.Ordinal)
            && string.Equals(log.CardDbContentHash, archive.CardDbContentHash, StringComparison.Ordinal)
            && string.Equals(log.RngAlgorithmVersion, archive.RngAlgorithmVersion, StringComparison.Ordinal)
            && string.Equals(log.DeterministicIdVersion, archive.DeterministicIdVersion, StringComparison.Ordinal)
            && string.Equals(log.OpeningProtocolVersion, archive.OpeningProtocolVersion, StringComparison.Ordinal)
            && string.Equals(log.ReplayConfigSchema, archive.ReplayConfigSchema, StringComparison.Ordinal)
            && string.Equals(runtimeManifestHash, archive.ManifestHash, StringComparison.Ordinal);

    private static ReplayCoverageEntry Entry(
        string sourceId,
        string sourceFileHash,
        string? matchId,
        string? artifactId,
        string status,
        string reasonCode,
        string stage,
        string? replayDigest = null)
    {
        var stable = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            sourceId,
            sourceFileHash,
            matchId,
            artifactId,
            status,
            reasonCode,
            stage,
            replayDigest,
        }));
        return new ReplayCoverageEntry(
            sourceId,
            sourceFileHash,
            matchId,
            artifactId,
            status,
            reasonCode,
            stage,
            replayDigest,
            stable);
    }

    private static ReplayCoverageWorkerArtifact WorkerArtifact(
        string artifactId,
        bool entrypointAvailable,
        bool handshakeVerified,
        string reasonCode)
    {
        var stable = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            artifactId,
            entrypointAvailable,
            handshakeVerified,
            reasonCode,
        }));
        return new ReplayCoverageWorkerArtifact(
            artifactId,
            entrypointAvailable,
            handshakeVerified,
            reasonCode,
            stable);
    }

    private static IReadOnlyList<LogSource> EnumerateLogSources(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            RejectLink(fullPath, "日志文件");
            return Array.AsReadOnly(new[] { new LogSource(Path.GetFileName(fullPath), fullPath) });
        }
        if (!Directory.Exists(fullPath)) return Array.Empty<LogSource>();
        RejectLinkedAncestors(fullPath, "日志根目录");
        RejectLink(fullPath, "日志根目录");

        var result = new List<LogSource>();
        var pending = new Stack<string>();
        pending.Push(fullPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.Ordinal))
            {
                RejectLink(entry, "日志扫描项");
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                    continue;
                }
                if (!string.Equals(Path.GetExtension(entry), ".jsonl", StringComparison.OrdinalIgnoreCase))
                    continue;
                var sourceId = Path.GetRelativePath(fullPath, entry)
                    .Replace('\\', '/')
                    .Normalize(NormalizationForm.FormC);
                result.Add(new LogSource(sourceId, entry));
            }
        }
        return result.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray();
    }

    private static void RejectLink(string path, string context)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new ReplayArtifactArchiveException($"{context}不能是符号链接或重解析点：{path}");
    }

    private static void RejectLinkedAncestors(string path, string context)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        if (!current.Exists) current = current.Parent;
        while (current is not null)
        {
            if (current.Exists
                && ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null))
                throw new ReplayArtifactArchiveException(
                    $"{context}经过符号链接或重解析点：{current.FullName}");
            current = current.Parent;
        }
    }

    private static void EnsureOutputOutsideArchive(string archiveRoot, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var archive = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archiveRoot));
        var output = Path.GetFullPath(outputPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(output, archive, comparison)
            || output.StartsWith(archive + Path.DirectorySeparatorChar, comparison))
            throw new ReplayArtifactArchiveException("覆盖报告和测试候选 catalog 不得写入不可变归档根目录。");
    }

    private static void EnsureDistinctOutputs(params string[] paths)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var fullPaths = paths.Select(Path.GetFullPath).ToArray();
        if (fullPaths.Distinct(comparer).Count() != fullPaths.Length)
            throw new ReplayArtifactArchiveException("JSON、Markdown 与测试候选 catalog 输出路径必须互不相同。");
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ReplayArtifactArchiveException($"无法解析输出目录：{path}");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.next-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private sealed record LogSource(string SourceId, string FullPath);
}
