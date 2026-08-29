using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Actions;

namespace GrandUMI.Training;

public static class TrainingDatasetReasonCodes
{
    public const string AcceptedActionNotInLegalSet = "accepted_action_not_in_legal_set";
    public const string HumanSourceNotReplayVerified = "human_source_not_replay_verified";
    public const string ObservationPrivacyViolation = "observation_privacy_violation";
}

public enum TrainingDatasetSourceKind
{
    Synthetic,
    HumanVerified,
}

public sealed record TrainingDatasetLineage(
    string MatchId,
    string SourceId,
    string SourceFileHash,
    string EngineArtifactId,
    long ActionOrderSeq,
    string GroupKey,
    TrainingDatasetSourceKind SourceKind,
    bool ReplayVerified);

public sealed record TrainingActionTaken(
    string ActionId,
    string Action,
    JsonElement Data);

public sealed record TrainingDatasetSample(
    string Schema,
    string SampleId,
    string DedupeKey,
    string Split,
    TrainingObservation Observation,
    LegalActionSet LegalActions,
    TrainingActionTaken ActionTaken,
    TrainingDatasetLineage Lineage);

public sealed record TrainingDatasetQuarantine(
    string MatchId,
    string ReasonCode,
    long? ActionOrderSeq,
    string StableHash);

public sealed record TrainingDatasetMatchResult(
    string MatchId,
    IReadOnlyList<TrainingDatasetSample> Samples,
    TrainingDatasetQuarantine? Quarantine)
{
    public bool IsEligible => Quarantine is null;
}

public sealed record TrainingDatasetManifest(
    string Schema,
    string ObservationSchema,
    string ActionSchema,
    string ActionSpaceHash,
    int EligibleMatches,
    int QuarantinedMatches,
    int SamplesBeforeDedupe,
    int SamplesAfterDedupe,
    IReadOnlyDictionary<string, int> SplitCounts,
    IReadOnlyDictionary<string, int> SourceCounts,
    IReadOnlyDictionary<string, int> QuarantineReasonCounts,
    string SamplesSha256,
    string ManifestHash);

/// <summary>单局全有或全无收集器；任一真人 accepted 不在当时合法集合中就清空并隔离整局。</summary>
public sealed class TrainingDatasetMatchCollector
{
    private readonly TrainingDatasetLineage _baseLineage;
    private readonly List<TrainingDatasetSample> _samples = new();
    private TrainingDatasetQuarantine? _quarantine;

    public TrainingDatasetMatchCollector(TrainingDatasetLineage baseLineage)
    {
        _baseLineage = baseLineage;
        if (baseLineage.SourceKind == TrainingDatasetSourceKind.HumanVerified
            && !baseLineage.ReplayVerified)
            Isolate(TrainingDatasetReasonCodes.HumanSourceNotReplayVerified, null);
    }

    /// <summary>必须在动作应用前调用。System 动作只推进重放，不产生标签。</summary>
    public bool ObserveAcceptedAction(
        GameState state,
        int actorSeat,
        string action,
        JsonElement data,
        GameActionSource source,
        long actionOrderSeq)
    {
        if (_quarantine is not null) return false;
        if (source == GameActionSource.System) return true;
        if (!LegalActionSpace.IsPolicyAction(action)) return true;

        var legal = LegalActionService.Enumerate(state, actorSeat, LegalActionPurpose.Training);
        if (!LegalActionService.Contains(legal, action, data, out var actionId, out _))
        {
            Isolate(TrainingDatasetReasonCodes.AcceptedActionNotInLegalSet, actionOrderSeq);
            return false;
        }

        TrainingObservation observation;
        try
        {
            observation = TrainingObservationBuilder.Build(state, actorSeat);
        }
        catch (InvalidOperationException)
        {
            Isolate(TrainingDatasetReasonCodes.ObservationPrivacyViolation, actionOrderSeq);
            return false;
        }
        LegalActionService.TryCanonicalize(action, data, out var canonical, out _);
        var lineage = _baseLineage with { ActionOrderSeq = actionOrderSeq };
        var dedupeKey = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            observation = observation.StableHash,
            legal = legal.StableHash,
            actionId,
        }));
        var sampleId = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            dedupeKey,
            lineage.MatchId,
            lineage.SourceFileHash,
            actionOrderSeq,
        }));
        _samples.Add(new TrainingDatasetSample(
            TrainingDatasetExporter.SampleSchema,
            sampleId,
            dedupeKey,
            TrainingDatasetSplitPlanner.Assign(lineage.GroupKey),
            observation,
            legal,
            new TrainingActionTaken(actionId!, action, canonical),
            lineage));
        return true;
    }

    public TrainingDatasetMatchResult Complete()
        => new(
            _baseLineage.MatchId,
            _quarantine is null
                ? Array.AsReadOnly(_samples.ToArray())
                : Array.Empty<TrainingDatasetSample>(),
            _quarantine);

    private void Isolate(string reasonCode, long? actionOrderSeq)
    {
        _samples.Clear();
        var hash = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            _baseLineage.MatchId,
            reasonCode,
            actionOrderSeq,
        }));
        _quarantine = new TrainingDatasetQuarantine(
            _baseLineage.MatchId,
            reasonCode,
            actionOrderSeq,
            hash);
    }
}

public static class TrainingDatasetSplitPlanner
{
    /// <summary>同一匿名账号组永远进入同一 split，整局样本不会跨 split。</summary>
    public static string Assign(string opaqueGroupKey)
    {
        if (string.IsNullOrWhiteSpace(opaqueGroupKey))
            throw new ArgumentException("数据集 groupKey 不能为空", nameof(opaqueGroupKey));
        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            $"grandumi.dataset.split.v1\n{opaqueGroupKey}"));
        var bucket = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(digest) % 100;
        return bucket < 80 ? "train" : bucket < 90 ? "validation" : "test";
    }
}

public static class TrainingDatasetExporter
{
    public const string SampleSchema = "grandumi.training_sample.v1";
    public const string ManifestSchema = "grandumi.training_dataset_manifest.v1";

    public static TrainingDatasetManifest Export(
        IEnumerable<TrainingDatasetMatchResult> matches,
        string samplesPath,
        string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(matches);
        EnsureDistinctFiles(samplesPath, manifestPath);
        var orderedMatches = matches.OrderBy(match => match.MatchId, StringComparer.Ordinal).ToArray();
        var samplesBeforeDedupe = 0;
        var deduped = new Dictionary<string, TrainingDatasetSample>(StringComparer.Ordinal);
        foreach (var match in orderedMatches.Where(match => match.IsEligible))
        {
            foreach (var sample in match.Samples)
            {
                samplesBeforeDedupe++;
                if (!deduped.TryGetValue(sample.DedupeKey, out var existing)
                    || string.CompareOrdinal(sample.SampleId, existing.SampleId) < 0)
                    deduped[sample.DedupeKey] = sample;
            }
        }
        var samples = deduped.Values
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .ToArray();
        // 逐行规范化、写盘并增量计算哈希，避免同时保留 byte[][]、MemoryStream 和整份 byte[]。
        var samplesSha256 = WriteSamplesAtomic(samplesPath, samples);
        var withoutHash = new TrainingDatasetManifest(
            ManifestSchema,
            TrainingObservationBuilder.Schema,
            LegalActionSpace.Schema,
            LegalActionSpace.ActionSpaceHash,
            orderedMatches.Count(match => match.IsEligible),
            orderedMatches.Count(match => !match.IsEligible),
            samplesBeforeDedupe,
            samples.Length,
            Count(samples.Select(sample => sample.Split)),
            Count(samples.Select(sample => sample.Lineage.SourceKind.ToString().ToLowerInvariant())),
            Count(orderedMatches.Where(match => match.Quarantine is not null)
                .Select(match => match.Quarantine!.ReasonCode)),
            samplesSha256,
            ManifestHash: string.Empty);
        var manifestHash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, JsonOptions),
            "manifestHash");
        var manifest = withoutHash with { ManifestHash = manifestHash };
        WriteAtomic(manifestPath, CanonicalJson.Encode(JsonSerializer.SerializeToElement(manifest, JsonOptions)));
        return manifest;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static IReadOnlyDictionary<string, int> Count(IEnumerable<string> values)
        => new SortedDictionary<string, int>(
            values.GroupBy(value => value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static void EnsureDistinctFiles(string first, string second)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (comparer.Equals(Path.GetFullPath(first), Path.GetFullPath(second)))
            throw new InvalidOperationException("样本与 manifest 不能写入同一文件");
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"无法解析输出目录：{path}");
        Directory.CreateDirectory(directory);
        var next = Path.Combine(directory, $".{Path.GetFileName(path)}.next-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(next, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(next)) File.Delete(next);
        }
    }

    private static string WriteSamplesAtomic(
        string path,
        IReadOnlyList<TrainingDatasetSample> samples)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"无法解析样本输出目录：{path}");
        Directory.CreateDirectory(directory);
        var next = Path.Combine(directory, $".{Path.GetFileName(path)}.next-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var stream = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var sample in samples)
                {
                    var line = CanonicalJson.Encode(JsonSerializer.SerializeToElement(sample, JsonOptions));
                    stream.Write(line);
                    stream.WriteByte((byte)'\n');
                    hash.AppendData(line);
                    hash.AppendData("\n"u8);
                }
                stream.Flush(flushToDisk: true);
            }
            var digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            File.Move(next, fullPath, overwrite: true);
            return digest;
        }
        finally
        {
            if (File.Exists(next)) File.Delete(next);
        }
    }
}

/// <summary>测试服 accepted 日志附带的动作前哈希开关；正式服默认关闭。</summary>
public static class TrainingDecisionContextFeature
{
    public static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("GRANDUMI_TRAINING_DECISION_CONTEXT_LOG"),
            "1",
            StringComparison.Ordinal);
}
