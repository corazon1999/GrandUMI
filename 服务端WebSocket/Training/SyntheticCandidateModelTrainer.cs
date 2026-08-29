using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrandUMI.Game.Actions;

namespace GrandUMI.Training;

public sealed record SyntheticModelEvaluation(
    int SampleCount,
    int HeldOutCount,
    double HeldOutActionTypeAccuracy,
    int IllegalSelectionCount);

public sealed record SyntheticCandidateModelManifest(
    string Schema,
    string ModelId,
    string Source,
    string DatasetManifestHash,
    string SamplesSha256,
    string ActionSpaceHash,
    string FeatureSchema,
    IReadOnlyDictionary<string, double> ActionBias,
    SyntheticModelEvaluation Evaluation,
    bool HumanTrainingEvidence,
    bool ProductionEligible,
    string ModelHash);

/// <summary>对 synthetic JSONL 做确定性候选动作类型行为克隆；绝不把结果标记为真人模型。</summary>
public static class SyntheticCandidateModelTrainer
{
    public const string Schema = "grandumi.synthetic_candidate_model.v1";
    public const string FeatureSchema = "grandumi.candidate_features.v1";
    public const string ComputeDevice = "cpu";
    public const int DefaultSampleBudget = 4_000;
    public const int MaximumSampleBudget = 50_000;
    private const int MaximumSampleLineCharacters = 2 * 1024 * 1024;

    public static SyntheticCandidateModelManifest Train(
        string samplesPath,
        string datasetManifestPath,
        string outputPath,
        int sampleBudget = DefaultSampleBudget)
    {
        if (sampleBudget is < 1 or > MaximumSampleBudget)
            throw new ArgumentOutOfRangeException(
                nameof(sampleBudget),
                $"样本预算必须在 1..{MaximumSampleBudget} 之间");
        var datasetManifest = ReadDatasetManifest(datasetManifestPath);
        if (!string.Equals(datasetManifest.ActionSpaceHash, LegalActionSpace.ActionSpaceHash, StringComparison.Ordinal))
            throw new InvalidDataException("数据集 actionSpaceHash 与当前训练器不一致");
        var samplesSha256 = InspectSampleFile(samplesPath);
        if (!string.Equals(samplesSha256, datasetManifest.SamplesSha256, StringComparison.Ordinal))
            throw new InvalidDataException("样本文件哈希与数据集 manifest 不一致");

        var samples = ReadSyntheticSampleSummary(samplesPath, sampleBudget);
        if (samples.SampleCount == 0) throw new InvalidDataException("synthetic 数据集没有可训练样本");
        if (samples.SampleCount != datasetManifest.SamplesAfterDedupe)
            throw new InvalidDataException("样本行数与数据集 manifest 不一致");
        var counts = samples.TrainingCount == 0 ? samples.AllActionCounts : samples.TrainingActionCounts;
        var trainingCount = samples.TrainingCount == 0 ? samples.SampleCount : samples.TrainingCount;
        var denominator = trainingCount + 1.0;
        var weights = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var action in LegalActionNames())
        {
            // 拉普拉斯平滑后的对数频率；缩放后保留可解释、稳定的小数权重。
            var probability = (counts.GetValueOrDefault(action) + 1.0) /
                              (denominator + LegalActionNames().Count);
            weights[action] = Math.Round(Math.Log(probability) * 10.0, 6, MidpointRounding.ToEven);
        }

        var majority = weights.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        var accuracy = samples.HeldOutCount == 0
            ? 0
            : samples.HeldOutActionCounts.GetValueOrDefault(majority) / (double)samples.HeldOutCount;
        var evaluation = new SyntheticModelEvaluation(
            samples.SampleCount,
            samples.HeldOutCount,
            Math.Round(accuracy, 6, MidpointRounding.ToEven),
            IllegalSelectionCount: 0);
        var withoutHash = new SyntheticCandidateModelManifest(
            Schema,
            "grandumi-first-synthetic-candidate-model-v1",
            "synthetic_engineering_fixture",
            datasetManifest.ManifestHash,
            datasetManifest.SamplesSha256,
            LegalActionSpace.ActionSpaceHash,
            FeatureSchema,
            weights,
            evaluation,
            HumanTrainingEvidence: false,
            ProductionEligible: false,
            ModelHash: string.Empty);
        var hash = CanonicalJson.Hash(
            JsonSerializer.SerializeToElement(withoutHash, JsonOptions),
            "modelHash");
        var manifest = withoutHash with { ModelHash = hash };
        WriteAtomic(outputPath, CanonicalJson.Encode(JsonSerializer.SerializeToElement(manifest, JsonOptions)));
        return manifest;
    }

    public static SyntheticCandidateModelManifest Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var canonicalBytes = CanonicalJson.Encode(document.RootElement);
        if (!IsCanonicalFile(bytes, canonicalBytes))
            throw new InvalidDataException("模型 manifest 必须是规范 JSON，最多只能带一个文件末尾换行");
        var manifest = JsonSerializer.Deserialize<SyntheticCandidateModelManifest>(canonicalBytes, JsonOptions)
            ?? throw new InvalidDataException("模型 manifest 为空");
        if (!string.Equals(manifest.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(manifest.ModelId, "grandumi-first-synthetic-candidate-model-v1", StringComparison.Ordinal)
            || !string.Equals(manifest.Source, "synthetic_engineering_fixture", StringComparison.Ordinal)
            || !string.Equals(manifest.ActionSpaceHash, LegalActionSpace.ActionSpaceHash, StringComparison.Ordinal)
            || !string.Equals(manifest.FeatureSchema, FeatureSchema, StringComparison.Ordinal)
            || manifest.HumanTrainingEvidence
            || manifest.ProductionEligible
            || manifest.Evaluation.IllegalSelectionCount != 0)
            throw new InvalidDataException("模型 manifest 身份或 synthetic 安全边界不合法");
        var actionNames = LegalActionNames();
        if (manifest.ActionBias is null
            || manifest.ActionBias.Count != actionNames.Count
            || actionNames.Any(action => !manifest.ActionBias.TryGetValue(action, out var weight)
                                         || !double.IsFinite(weight)))
            throw new InvalidDataException("模型 actionBias 与当前动作空间不完整或包含非法权重");
        var actualHash = CanonicalJson.Hash(document.RootElement, "modelHash");
        if (!string.Equals(actualHash, manifest.ModelHash, StringComparison.Ordinal))
            throw new InvalidDataException("模型 manifest 自哈希不一致");
        return manifest;
    }

    private static bool IsCanonicalFile(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> canonical)
    {
        if (bytes.SequenceEqual(canonical)) return true;
        if (bytes.Length == canonical.Length + 1
            && bytes[^1] == (byte)'\n'
            && bytes[..^1].SequenceEqual(canonical))
            return true;
        return bytes.Length == canonical.Length + 2
               && bytes[^2] == (byte)'\r'
               && bytes[^1] == (byte)'\n'
               && bytes[..^2].SequenceEqual(canonical);
    }

    private static TrainingDatasetManifest ReadDatasetManifest(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        if (!CanonicalJson.Encode(document.RootElement).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("数据集 manifest 必须是规范 JSON");
        var manifest = JsonSerializer.Deserialize<TrainingDatasetManifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException("数据集 manifest 为空");
        if (!string.Equals(manifest.Schema, TrainingDatasetExporter.ManifestSchema, StringComparison.Ordinal)
            || !string.Equals(
                CanonicalJson.Hash(document.RootElement, "manifestHash"),
                manifest.ManifestHash,
                StringComparison.Ordinal))
            throw new InvalidDataException("数据集 manifest schema 或自哈希无效");
        return manifest;
    }

    private static string InspectSampleFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0) throw new InvalidDataException("样本 JSONL 为空");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        byte last = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            if (Array.IndexOf(buffer, (byte)'\r', 0, read) >= 0)
                throw new InvalidDataException("样本 JSONL 只能使用 LF 换行");
            last = buffer[read - 1];
        }
        if (last != (byte)'\n') throw new InvalidDataException("样本 JSONL 必须以换行结束");
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static SyntheticSampleSummary ReadSyntheticSampleSummary(string path, int sampleBudget)
    {
        var allCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var trainingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var heldOutCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var sampleCount = 0;
        var trainingCount = 0;
        var heldOutCount = 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) throw new InvalidDataException("样本 JSONL 不允许空行");
            if (line.Length > MaximumSampleLineCharacters)
                throw new InvalidDataException($"样本 JSONL 单行超过 {MaximumSampleLineCharacters} 字符上限");
            using var document = JsonDocument.Parse(line);
            var lineBytes = Encoding.UTF8.GetBytes(line);
            if (!CanonicalJson.Encode(document.RootElement).AsSpan().SequenceEqual(lineBytes))
                throw new InvalidDataException("样本行不是规范 JSON");
            var sample = JsonSerializer.Deserialize<TrainingDatasetSample>(line, JsonOptions)
                ?? throw new InvalidDataException("样本行为空");
            if (sample.Lineage.SourceKind != TrainingDatasetSourceKind.Synthetic
                || sample.Lineage.ReplayVerified
                || !string.Equals(sample.Schema, TrainingDatasetExporter.SampleSchema, StringComparison.Ordinal)
                || !string.Equals(sample.LegalActions.ActionSpaceHash, LegalActionSpace.ActionSpaceHash, StringComparison.Ordinal))
                throw new InvalidDataException("训练器只接受明确标记的 synthetic 当前动作空间样本");
            sampleCount++;
            if (sampleCount > sampleBudget)
                throw new InvalidDataException($"synthetic 样本数超过低内存预算 {sampleBudget}");
            Increment(allCounts, sample.ActionTaken.Action);
            if (string.Equals(sample.Split, "train", StringComparison.Ordinal))
            {
                trainingCount++;
                Increment(trainingCounts, sample.ActionTaken.Action);
            }
            else
            {
                heldOutCount++;
                Increment(heldOutCounts, sample.ActionTaken.Action);
            }
        }
        return new SyntheticSampleSummary(
            sampleCount,
            trainingCount,
            heldOutCount,
            allCounts,
            trainingCounts,
            heldOutCounts);
    }

    private static void Increment(IDictionary<string, int> counts, string action)
        => counts[action] = counts.TryGetValue(action, out var count) ? count + 1 : 1;

    private sealed record SyntheticSampleSummary(
        int SampleCount,
        int TrainingCount,
        int HeldOutCount,
        IReadOnlyDictionary<string, int> AllActionCounts,
        IReadOnlyDictionary<string, int> TrainingActionCounts,
        IReadOnlyDictionary<string, int> HeldOutActionCounts);

    private static IReadOnlyList<string> LegalActionNames()
        =>
        [
            "ChooseFirstPlayer", "Mulligan", "PromptResponse", "PlayCard", "AttachDon", "Attack",
            "UseEffect", "DeclareBlocker", "PassBlock", "PlayCounter", "PassCounter", "EndTurn",
        ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"无法解析模型输出目录：{path}");
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
}
