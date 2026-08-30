using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;
using GrandUMI.Game.AI;

namespace GrandUMI.Training;

/// <summary>工程用 synthetic 自博弈、JSONL 导出与首个候选模型训练命令；输出永远标记为非真人。</summary>
public static class SyntheticTrainingCommand
{
    internal const int DefaultMatches = 4;
    internal const int DefaultMaxDecisions = 1_000;
    internal const int DefaultManagedMemoryBudgetMb = 256;
    internal const int MinimumManagedMemoryBudgetMb = 128;
    internal const int MaximumManagedMemoryBudgetMb = 2_048;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            EnsureLocalOutputPolicy(options.OutputDirectory);
            var resources = new SyntheticTrainingResourceGuard(options.ManagedMemoryBudgetMb);
            LoadRules();
            resources.ThrowIfExceeded("加载卡牌与效果规则后");
            Console.Error.WriteLine(
                $"[AI 训练] 低内存模式：设备={SyntheticCandidateModelTrainer.ComputeDevice.ToUpperInvariant()}，" +
                $"串行对局，样本上限={options.SampleBudget}，托管堆软上限={options.ManagedMemoryBudgetMb} MiB");
            var results = new List<TrainingDatasetMatchResult>();
            var collectedSamples = 0;
            for (var matchIndex = 0; matchIndex < options.Matches; matchIndex++)
            {
                var result = await RunSelfPlayAsync(
                    matchIndex,
                    options.MaxDecisions,
                    collectedSamples,
                    options.SampleBudget,
                    resources);
                collectedSamples += result.Samples.Count;
                if (collectedSamples > options.SampleBudget)
                    throw new InvalidOperationException($"synthetic 样本数超过低内存预算 {options.SampleBudget}");
                results.Add(result);
                resources.ThrowIfExceeded($"完成第 {matchIndex + 1} 局后");
            }

            Directory.CreateDirectory(options.OutputDirectory);
            var samplesPath = Path.Combine(options.OutputDirectory, "synthetic-samples.v1.jsonl");
            var datasetManifestPath = Path.Combine(options.OutputDirectory, "synthetic-dataset-manifest.v1.json");
            var modelPath = Path.Combine(options.OutputDirectory, "first-synthetic-model.v1.json");
            var dataset = TrainingDatasetExporter.Export(results, samplesPath, datasetManifestPath);
            resources.ThrowIfExceeded("导出数据集后");
            var model = SyntheticCandidateModelTrainer.Train(
                samplesPath,
                datasetManifestPath,
                modelPath,
                options.SampleBudget);
            resources.ThrowIfExceeded("生成候选模型后");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "ok",
                source = "synthetic",
                computeDevice = SyntheticCandidateModelTrainer.ComputeDevice,
                gpuAcceleration = false,
                resourceProfile = "low-memory",
                managedMemoryBudgetMb = options.ManagedMemoryBudgetMb,
                sampleBudget = options.SampleBudget,
                peakManagedMemoryMb = Math.Round(
                    resources.PeakManagedBytes / (1024d * 1024d),
                    1,
                    MidpointRounding.AwayFromZero),
                humanTrainingEvidence = false,
                productionEligible = false,
                matches = options.Matches,
                dataset.SamplesAfterDedupe,
                dataset.ManifestHash,
                model.ModelHash,
                model.Evaluation,
                samplesPath,
                datasetManifestPath,
                modelPath,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"synthetic 训练失败：{ex.Message}");
            return 1;
        }
    }

    private static async Task<TrainingDatasetMatchResult> RunSelfPlayAsync(
        int matchIndex,
        int maxDecisions,
        int previouslyCollectedSamples,
        int sampleBudget,
        SyntheticTrainingResourceGuard resources)
    {
        var seed = 202608290 + matchIndex;
        var firstPlayer = matchIndex % 2;
        var deck = BuildLegalDeck("OP15-001");
        var matchId = $"synthetic-{seed}";
        var engine = new GameEngine(
            matchId,
            ("synthetic-session-0", "synthetic-account-0", deck),
            ("synthetic-session-1", "synthetic-account-1", deck),
            firstPlayer,
            rngSeed: seed);
        var collector = new TrainingDatasetMatchCollector(new TrainingDatasetLineage(
            matchId,
            $"synthetic/self-play/{matchId}",
            CanonicalJson.Sha256Utf8($"{matchId}\n{seed}\n{deck}"),
            "synthetic-current-process",
            0,
            $"synthetic-group-{matchIndex}",
            TrainingDatasetSourceKind.Synthetic,
            ReplayVerified: false));
        var policy = new SyntheticBaselinePolicy();
        var fallback = new DeterministicSafePolicy();
        long actionOrder = 0;
        while (!engine.State.IsGameOver && actionOrder < maxDecisions)
        {
            await engine.WaitSettledAsync();
            var actor = DecisionActor(engine.State);
            var decision = await AiDecisionCoordinator.DecideAsync(
                engine.State,
                actor,
                policy,
                fallback,
                TimeSpan.FromMilliseconds(200));
            if (decision is null)
                throw new InvalidOperationException($"{matchId} 在未终局时没有合法动作");
            actionOrder++;
            if (previouslyCollectedSamples + actionOrder > sampleBudget)
                throw new InvalidOperationException($"synthetic 样本数超过低内存预算 {sampleBudget}");
            if (!collector.ObserveAcceptedAction(
                    engine.State,
                    actor,
                    decision.Action,
                    decision.Data,
                    GameActionSource.Player,
                    actionOrder))
                throw new InvalidOperationException($"{matchId} synthetic 教师动作未命中 LegalActionSet");
            if (!engine.HandleAction(
                    actor,
                    decision.Action,
                    decision.Data,
                    source: GameActionSource.System))
                throw new InvalidOperationException($"{matchId} synthetic 教师动作被 HandleAction 拒绝");
            if (actionOrder % 32 == 0)
                resources.ThrowIfExceeded($"{matchId} 第 {actionOrder} 次决策后");
        }
        await engine.WaitSettledAsync();
        if (!engine.State.IsGameOver)
            throw new TimeoutException($"{matchId} 超过 {maxDecisions} 次决策仍未终局");
        return collector.Complete();
    }

    private static int DecisionActor(GameState state)
    {
        if (state.PendingPrompt is { } prompt) return prompt.PlayerIndex;
        if (!state.StartingPlayerChosen) return state.StartingPlayerChooser;
        if (!state.Players[0].MulliganDone) return 0;
        if (!state.Players[1].MulliganDone) return 1;
        if (state.CurrentBattle is { } battle
            && state.Phase is Phase.BattleBlock or Phase.BattleCounter)
            return battle.DefenderPlayerIndex;
        return state.CurrentTurnPlayer;
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)
            ?? throw new InvalidOperationException($"找不到 synthetic 领航：{leaderNumber}");
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }

    private static void LoadRules()
    {
        var root = FindRepositoryRoot();
        CardDatabase.LoadFrom(Path.Combine(root, "卡牌数据"));
        DslInterpreter.LoadDirectory(
            Path.Combine(root, "服务端WebSocket", "Effects", "Definitions"),
            "synthetic-training",
            failClosed: true);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "卡牌数据"))
                && Directory.Exists(Path.Combine(current.FullName, "服务端WebSocket")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("找不到 GrandUMI 仓库根目录");
    }

    private static void EnsureLocalOutputPolicy(string outputDirectory)
    {
        if (!OperatingSystem.IsWindows()) return;
        var output = Path.GetFullPath(outputDirectory);
        var allowed = Path.GetFullPath(@"E:\GrandUMI-Temp\");
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Windows synthetic 训练输出必须位于 E:\\GrandUMI-Temp，实际为：{output}");
    }

    internal static SyntheticTrainingOptions ParseOptions(IReadOnlyList<string> args)
    {
        string? output = null;
        var matches = DefaultMatches;
        var maxDecisions = DefaultMaxDecisions;
        var sampleBudget = SyntheticCandidateModelTrainer.DefaultSampleBudget;
        var managedMemoryBudgetMb = DefaultManagedMemoryBudgetMb;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--output-dir" when index + 1 < args.Count:
                    output = Path.GetFullPath(args[++index]);
                    break;
                case "--matches" when index + 1 < args.Count
                    && int.TryParse(args[++index], out var parsedMatches)
                    && parsedMatches is >= 1 and <= 100:
                    matches = parsedMatches;
                    break;
                case "--max-decisions" when index + 1 < args.Count
                    && int.TryParse(args[++index], out var parsedMax)
                    && parsedMax is >= 100 and <= 10_000:
                    maxDecisions = parsedMax;
                    break;
                case "--sample-budget" when index + 1 < args.Count
                    && int.TryParse(args[++index], out var parsedSampleBudget)
                    && parsedSampleBudget is >= 1 and <= SyntheticCandidateModelTrainer.MaximumSampleBudget:
                    sampleBudget = parsedSampleBudget;
                    break;
                case "--managed-memory-budget-mb" when index + 1 < args.Count
                    && int.TryParse(args[++index], out var parsedMemoryBudget)
                    && parsedMemoryBudget is >= MinimumManagedMemoryBudgetMb and <= MaximumManagedMemoryBudgetMb:
                    managedMemoryBudgetMb = parsedMemoryBudget;
                    break;
                case "--compute-device" when index + 1 < args.Count:
                    var requestedDevice = args[++index];
                    if (!string.Equals(
                            requestedDevice,
                            SyntheticCandidateModelTrainer.ComputeDevice,
                            StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(
                            $"当前 synthetic 训练器只支持 CPU，拒绝设备：{requestedDevice}");
                    break;
                default:
                    throw new ArgumentException(
                        "用法：GrandUMIServer --training-synthetic --output-dir <E盘目录> " +
                        "[--compute-device cpu] [--matches 1..100] [--max-decisions 100..10000] " +
                        $"[--sample-budget 1..{SyntheticCandidateModelTrainer.MaximumSampleBudget}] " +
                        $"[--managed-memory-budget-mb {MinimumManagedMemoryBudgetMb}..{MaximumManagedMemoryBudgetMb}]");
            }
        }
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("必须提供 --output-dir");
        var maximumPossibleSamples = (long)matches * maxDecisions;
        if (maximumPossibleSamples > sampleBudget)
            throw new ArgumentException(
                $"matches×max-decisions 的最坏样本数 {maximumPossibleSamples} 超过样本预算 {sampleBudget}；" +
                "请减少对局或单局决策数，或显式提高 --sample-budget");
        return new SyntheticTrainingOptions(
            output,
            matches,
            maxDecisions,
            sampleBudget,
            managedMemoryBudgetMb);
    }
}

internal sealed record SyntheticTrainingOptions(
    string OutputDirectory,
    int Matches,
    int MaxDecisions,
    int SampleBudget,
    int ManagedMemoryBudgetMb);

internal sealed class SyntheticTrainingResourceGuard
{
    private readonly long _managedMemoryLimitBytes;

    public SyntheticTrainingResourceGuard(int managedMemoryBudgetMb)
        => _managedMemoryLimitBytes = managedMemoryBudgetMb * 1024L * 1024L;

    public long PeakManagedBytes { get; private set; }

    public void ThrowIfExceeded(string stage)
    {
        var current = GC.GetTotalMemory(forceFullCollection: false);
        PeakManagedBytes = Math.Max(PeakManagedBytes, current);
        if (current <= _managedMemoryLimitBytes) return;

        // 低内存配置允许更慢：超预算时先做一次阻塞回收，再决定是否安全失败。
        current = GC.GetTotalMemory(forceFullCollection: true);
        PeakManagedBytes = Math.Max(PeakManagedBytes, current);
        if (current > _managedMemoryLimitBytes)
            throw new InvalidOperationException(
                $"{stage}的托管堆约 {ToMiB(current):F1} MiB，超过低内存软上限 " +
                $"{ToMiB(_managedMemoryLimitBytes):F0} MiB；训练已安全停止");
    }

    private static double ToMiB(long bytes) => bytes / (1024d * 1024d);
}
