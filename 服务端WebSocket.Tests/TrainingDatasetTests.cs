using System.Security.Cryptography;
using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Actions;
using GrandUMI.Game.AI;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class TrainingDatasetTests
{
    [Fact]
    public void Synthetic训练配置_默认CPU低内存并拒绝GPU与超预算()
    {
        var output = Path.GetFullPath(@"E:\GrandUMI-Temp\server-tests\options-only");
        var defaults = SyntheticTrainingCommand.ParseOptions(["--output-dir", output]);

        Assert.Equal("cpu", SyntheticCandidateModelTrainer.ComputeDevice);
        Assert.Equal(4, defaults.Matches);
        Assert.Equal(1_000, defaults.MaxDecisions);
        Assert.Equal(4_000, defaults.SampleBudget);
        Assert.Equal(256, defaults.ManagedMemoryBudgetMb);

        var explicitCpu = SyntheticTrainingCommand.ParseOptions(
            ["--output-dir", output, "--compute-device", "CPU"]);
        Assert.Equal(defaults, explicitCpu);

        var gpu = Assert.Throws<ArgumentException>(() => SyntheticTrainingCommand.ParseOptions(
            ["--output-dir", output, "--compute-device", "cuda"]));
        Assert.Contains("只支持 CPU", gpu.Message, StringComparison.Ordinal);

        var overBudget = Assert.Throws<ArgumentException>(() => SyntheticTrainingCommand.ParseOptions(
            [
                "--output-dir", output,
                "--matches", "5",
                "--max-decisions", "1000",
                "--sample-budget", "4000",
            ]));
        Assert.Contains("最坏样本数", overBudget.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void System动作只推进不产标签_真人未验证整局隔离()
    {
        var state = PlayingState();
        var synthetic = new TrainingDatasetMatchCollector(Lineage(
            "synthetic-1", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        Assert.True(synthetic.ObserveAcceptedAction(
            state, 0, "EndTurn", JsonSerializer.SerializeToElement(new { }),
            GameActionSource.System, 1));
        Assert.Empty(synthetic.Complete().Samples);

        var unverifiedHuman = new TrainingDatasetMatchCollector(Lineage(
            "human-1", TrainingDatasetSourceKind.HumanVerified, replayVerified: false));
        var result = unverifiedHuman.Complete();
        Assert.False(result.IsEligible);
        Assert.Equal(TrainingDatasetReasonCodes.HumanSourceNotReplayVerified, result.Quarantine!.ReasonCode);
    }

    [Fact]
    public void Accepted不在当时枚举中_稳定原因码隔离整局并清除已有样本()
    {
        var state = PlayingState();
        var collector = new TrainingDatasetMatchCollector(Lineage(
            "coverage-fail", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        Assert.True(collector.ObserveAcceptedAction(
            state, 0, "EndTurn", JsonSerializer.SerializeToElement(new { }),
            GameActionSource.Player, 1));
        Assert.False(collector.ObserveAcceptedAction(
            state, 0, "Attack", JsonSerializer.SerializeToElement(new
            {
                attackerId = Guid.NewGuid().ToString(),
                targetIsLeader = true,
            }),
            GameActionSource.Player, 2));

        var result = collector.Complete();
        Assert.Empty(result.Samples);
        Assert.Equal(TrainingDatasetReasonCodes.AcceptedActionNotInLegalSet, result.Quarantine!.ReasonCode);
        Assert.Equal(2, result.Quarantine.ActionOrderSeq);
    }

    [Fact]
    public void JSONL导出_按匿名账号组稳定切分并去重()
    {
        var state = PlayingState();
        var first = new TrainingDatasetMatchCollector(Lineage(
            "synthetic-a", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        var second = new TrainingDatasetMatchCollector(Lineage(
            "synthetic-b", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        var action = JsonSerializer.SerializeToElement(new { });
        Assert.True(first.ObserveAcceptedAction(state, 0, "EndTurn", action, GameActionSource.Player, 1));
        Assert.True(second.ObserveAcceptedAction(state, 0, "EndTurn", action, GameActionSource.Player, 1));

        var root = NewGrandUmiTempDirectory("dataset");
        var samples = Path.Combine(root, "samples.jsonl");
        var manifestPath = Path.Combine(root, "manifest.json");
        try
        {
            var manifest = TrainingDatasetExporter.Export(
                new[] { first.Complete(), second.Complete() },
                samples,
                manifestPath);

            Assert.Equal(2, manifest.SamplesBeforeDedupe);
            Assert.Equal(1, manifest.SamplesAfterDedupe);
            Assert.Equal(2, manifest.EligibleMatches);
            Assert.StartsWith("sha256:", manifest.ManifestHash);
            Assert.StartsWith("sha256:", manifest.SamplesSha256);
            Assert.Equal(
                "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(samples))).ToLowerInvariant(),
                manifest.SamplesSha256);
            Assert.Single(File.ReadAllLines(samples));
            var line = File.ReadAllText(samples);
            Assert.DoesNotContain("secret", line, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(TrainingDatasetSplitPlanner.Assign("opaque-shared-group"),
                JsonDocument.Parse(line).RootElement.GetProperty("split").GetString());
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            Assert.StartsWith(Path.GetFullPath(@"E:\GrandUMI-Temp\"), fullRoot, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Synthetic模型_可重现加载且推理结果始终命中Mask()
    {
        var state = PlayingState();
        var collector = new TrainingDatasetMatchCollector(Lineage(
            "synthetic-model", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        Assert.True(collector.ObserveAcceptedAction(
            state,
            0,
            "EndTurn",
            JsonSerializer.SerializeToElement(new { }),
            GameActionSource.Player,
            1));

        var root = NewGrandUmiTempDirectory("model");
        var samples = Path.Combine(root, "samples.jsonl");
        var datasetManifest = Path.Combine(root, "dataset.json");
        var modelPath = Path.Combine(root, "model.json");
        try
        {
            TrainingDatasetExporter.Export([collector.Complete()], samples, datasetManifest);
            var trained = SyntheticCandidateModelTrainer.Train(samples, datasetManifest, modelPath);
            var loaded = SyntheticCandidateModelTrainer.Load(modelPath);

            Assert.Equal(trained.ModelHash, loaded.ModelHash);
            Assert.False(loaded.HumanTrainingEvidence);
            Assert.False(loaded.ProductionEligible);
            Assert.Equal(0, loaded.Evaluation.IllegalSelectionCount);
            Assert.Equal(12, loaded.ActionBias.Count);

            // 仓库文本文件允许且只允许一个末尾换行，不能因此退回内置模型。
            File.AppendAllText(modelPath, "\n", new System.Text.UTF8Encoding(false));
            Assert.Equal(loaded.ModelHash, SyntheticCandidateModelTrainer.Load(modelPath).ModelHash);

            var decision = await AiDecisionCoordinator.DecideAsync(
                state,
                0,
                new SyntheticBaselinePolicy(loaded),
                new DeterministicSafePolicy(),
                TimeSpan.FromSeconds(1));
            Assert.NotNull(decision);
            var legal = LegalActionService.Enumerate(state, 0, LegalActionPurpose.Inference);
            Assert.True(LegalActionService.Contains(
                legal,
                decision!.Action,
                decision.Data,
                out var actionId,
                out _));
            Assert.Equal(decision.ActionId, actionId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Synthetic模型_样本超过低内存预算时安全拒绝()
    {
        var firstState = PlayingState();
        var secondState = PlayingState();
        secondState.TurnCount++;
        var first = new TrainingDatasetMatchCollector(Lineage(
            "budget-first", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        var second = new TrainingDatasetMatchCollector(Lineage(
            "budget-second", TrainingDatasetSourceKind.Synthetic, replayVerified: false));
        var action = JsonSerializer.SerializeToElement(new { });
        Assert.True(first.ObserveAcceptedAction(
            firstState, 0, "EndTurn", action, GameActionSource.Player, 1));
        Assert.True(second.ObserveAcceptedAction(
            secondState, 0, "EndTurn", action, GameActionSource.Player, 1));

        var root = NewGrandUmiTempDirectory("model-budget");
        var samples = Path.Combine(root, "samples.jsonl");
        var datasetManifest = Path.Combine(root, "dataset.json");
        var modelPath = Path.Combine(root, "model.json");
        try
        {
            var manifest = TrainingDatasetExporter.Export(
                [first.Complete(), second.Complete()],
                samples,
                datasetManifest);
            Assert.Equal(2, manifest.SamplesAfterDedupe);

            var error = Assert.Throws<InvalidDataException>(() =>
                SyntheticCandidateModelTrainer.Train(
                    samples,
                    datasetManifest,
                    modelPath,
                    sampleBudget: 1));
            Assert.Contains("超过低内存预算 1", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(modelPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static GameState PlayingState()
    {
        var state = TestScene.New().Build();
        state.FirstPlayer = 0;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        state.OpeningStage = OpeningStage.Playing;
        state.Players[0].MulliganDone = true;
        state.Players[1].MulliganDone = true;
        return state;
    }

    private static TrainingDatasetLineage Lineage(
        string matchId,
        TrainingDatasetSourceKind source,
        bool replayVerified)
        => new(
            matchId,
            $"fixture/{matchId}.jsonl",
            $"sha256:{new string('a', 64)}",
            "synthetic-current",
            0,
            "opaque-shared-group",
            source,
            replayVerified);

    private static string NewGrandUmiTempDirectory(string purpose)
    {
        var baseDirectory = OperatingSystem.IsWindows()
            ? @"E:\GrandUMI-Temp\server-tests"
            : Path.Combine(Path.GetTempPath(), "GrandUMI-Temp", "server-tests");
        var root = Path.Combine(baseDirectory, $"{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        if (OperatingSystem.IsWindows())
            Assert.StartsWith(
                Path.GetFullPath(@"E:\GrandUMI-Temp\"),
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase);
        return root;
    }
}
