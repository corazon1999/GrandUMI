using System.Text.Json;
using GrandUMI.Game.Actions;
using GrandUMI.Training;

namespace GrandUMI.Game.AI;

/// <summary>
/// 首个可运行候选打分模型。权重仅由 synthetic 工程样本定义，不代表真人策略或 Gate B 证据。
/// </summary>
public sealed class SyntheticBaselinePolicy : IAiPolicy
{
    public const string Source = "synthetic_engineering_fixture";
    private readonly IReadOnlyDictionary<string, double> _learnedActionBias;
    public string PolicyId { get; }
    public string ModelHash { get; }
    public string ModelSource { get; }

    private static readonly IReadOnlyDictionary<string, double> BaseActionBias =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["ChooseFirstPlayer"] = 1_000,
            ["Mulligan"] = 1_000,
            ["PromptResponse"] = 1_000,
            ["PlayCard"] = 700,
            ["AttachDon"] = 600,
            ["Attack"] = 500,
            ["UseEffect"] = -200,
            ["DeclareBlocker"] = -100,
            ["PassBlock"] = 300,
            ["PlayCounter"] = -100,
            ["PassCounter"] = 300,
            ["EndTurn"] = 0,
        };

    public SyntheticBaselinePolicy()
    {
        PolicyId = "grandumi.synthetic.linear-candidate.v1";
        ModelSource = Source;
        _learnedActionBias = new Dictionary<string, double>(StringComparer.Ordinal);
        ModelHash = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            schema = "grandumi.candidate_linear_model.v1",
            policyId = PolicyId,
            source = Source,
            actionSpaceHash = LegalActionSpace.ActionSpaceHash,
            actionBias = BaseActionBias,
            featureWeights = new
            {
                promptSafeChoice = 200,
                playCharacter = 40,
                overflowTargetSpecified = 20,
                attachToLeader = 20,
                attachCount = 5,
                attackLeader = 100,
                passDefense = 50,
            },
        }));
    }

    public SyntheticBaselinePolicy(SyntheticCandidateModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.ActionSpaceHash, LegalActionSpace.ActionSpaceHash, StringComparison.Ordinal)
            || manifest.HumanTrainingEvidence
            || manifest.ProductionEligible)
            throw new InvalidDataException("只允许加载当前动作空间的 synthetic 非生产模型");
        PolicyId = manifest.ModelId;
        ModelHash = manifest.ModelHash;
        ModelSource = manifest.Source;
        _learnedActionBias = new Dictionary<string, double>(manifest.ActionBias, StringComparer.Ordinal);
    }

    public static SyntheticBaselinePolicy LoadConfiguredOrBuiltIn()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_AI_MODEL_MANIFEST");
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "Training", "Models", "first-synthetic-model.v1.json")
            : Path.GetFullPath(configured);
        if (!File.Exists(path)) return new SyntheticBaselinePolicy();
        try
        {
            return new SyntheticBaselinePolicy(SyntheticCandidateModelTrainer.Load(path));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AI] synthetic 模型加载失败，使用内置同动作空间基线：{ex.Message}");
            return new SyntheticBaselinePolicy();
        }
    }

    public ValueTask<AiPolicySelection> SelectAsync(
        AiPolicyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bestIndex = -1;
        var bestScore = double.NegativeInfinity;
        IReadOnlyList<string>? bestChoices = null;
        for (var index = 0; index < context.LegalActions.Candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.LegalActions.Mask.Bits[index] != 1) continue;
            var candidate = context.LegalActions.Candidates[index];
            var choices = MaterializePromptChoices(context.Observation, candidate);
            var score = Score(context.Observation, candidate, choices);
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = index;
            bestChoices = choices;
        }
        if (bestIndex < 0) throw new InvalidOperationException("合法动作 mask 为空");
        return ValueTask.FromResult(new AiPolicySelection(
            bestIndex,
            bestChoices,
            PolicyId,
            ModelHash));
    }

    private double Score(
        TrainingObservation observation,
        LegalActionCandidate candidate,
        IReadOnlyList<string>? selectedChoices)
    {
        var score = BaseActionBias.TryGetValue(candidate.Action, out var bias) ? bias : -10_000;
        if (_learnedActionBias.TryGetValue(candidate.Action, out var learned))
            score += learned * 0.01;
        var data = candidate.Data;
        switch (candidate.Action)
        {
            case "ChooseFirstPlayer":
                if (data.GetProperty("goFirst").GetBoolean()) score += 10;
                break;
            case "Mulligan":
                if (!data.GetProperty("redraw").GetBoolean()) score += 100;
                break;
            case "PromptResponse":
                score += PromptChoiceScore(observation, candidate, selectedChoices);
                break;
            case "PlayCard":
                if (data.TryGetProperty("overflowTrashCardId", out _)) score += 20;
                var handIndex = data.GetProperty("handIndex").GetInt32();
                var handCard = FindHandCard(observation, handIndex);
                if (handCard is { } card
                    && card.TryGetProperty("kind", out var kind)
                    && string.Equals(kind.GetString(), "Character", StringComparison.Ordinal))
                    score += 40;
                score -= handIndex * 0.001;
                break;
            case "AttachDon":
                if (string.Equals(data.GetProperty("targetId").GetString(), "leader", StringComparison.Ordinal))
                    score += 20;
                score += data.GetProperty("count").GetInt32() * 5;
                break;
            case "Attack":
                if (data.GetProperty("targetIsLeader").GetBoolean()) score += 100;
                break;
            case "PassBlock":
            case "PassCounter":
                score += 50;
                break;
        }
        return score;
    }

    private static double PromptChoiceScore(
        TrainingObservation observation,
        LegalActionCandidate candidate,
        IReadOnlyList<string>? selectedChoices)
    {
        var chosen = candidate.SelectionConstraint is null
            ? candidate.Data.TryGetProperty("chosen", out var chosenElement)
                ? chosenElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : Array.Empty<string>()
            : selectedChoices?.ToArray() ?? Array.Empty<string>();
        var promptKind = observation.Payload.TryGetProperty("prompt", out var prompt)
            && prompt.ValueKind == JsonValueKind.Object
            && prompt.TryGetProperty("kind", out var kind)
                ? kind.GetString() ?? string.Empty
                : string.Empty;
        if (string.Equals(promptKind, "LifeTrigger", StringComparison.Ordinal)
            && chosen.SequenceEqual(new[] { "hand" }, StringComparer.Ordinal))
            return 200;
        if (string.Equals(promptKind, "Option", StringComparison.Ordinal)
            && chosen.SequenceEqual(new[] { "1" }, StringComparer.Ordinal))
            return 200;
        return chosen.Length == 0 ? 50 : 0;
    }

    private static IReadOnlyList<string>? MaterializePromptChoices(
        TrainingObservation observation,
        LegalActionCandidate candidate)
    {
        if (candidate.SelectionConstraint is not { } constraint) return null;
        if (constraint.MinChoose == 0) return Array.Empty<string>();
        // 使用服务端冻结顺序选最小必选数；不读取任何隐藏状态，也不构造约束外 ID。
        return constraint.ValidChoices.Take(constraint.MinChoose).ToArray();
    }

    private static JsonElement? FindHandCard(TrainingObservation observation, int handIndex)
    {
        if (!observation.Payload.TryGetProperty("self", out var self)
            || !self.TryGetProperty("hand", out var hand)
            || hand.ValueKind != JsonValueKind.Object
            || !hand.TryGetProperty("cards", out var cards)
            || cards.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var card in cards.EnumerateArray())
            if (card.GetProperty("index").GetInt32() == handIndex) return card.Clone();
        return null;
    }
}

/// <summary>模型不可用时的最小推进策略；仍只从当前 LegalActionSet 选取。</summary>
public sealed class DeterministicSafePolicy : IAiPolicy
{
    public string PolicyId => "grandumi.safe-mask-fallback.v1";
    public string ModelHash => "none";

    public ValueTask<AiPolicySelection> SelectAsync(
        AiPolicyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preferred = new[]
        {
            "PassCounter",
            "PassBlock",
            "EndTurn",
            "PromptResponse",
            "Mulligan",
            "ChooseFirstPlayer",
        };
        foreach (var action in preferred)
        {
            for (var index = 0; index < context.LegalActions.Candidates.Count; index++)
            {
                if (context.LegalActions.Mask.Bits[index] != 1) continue;
                var candidate = context.LegalActions.Candidates[index];
                if (!string.Equals(candidate.Action, action, StringComparison.Ordinal)) continue;
                IReadOnlyList<string>? selected = null;
                if (candidate.SelectionConstraint is { } constraint)
                    selected = constraint.ValidChoices.Take(constraint.MinChoose).ToArray();
                return ValueTask.FromResult(new AiPolicySelection(index, selected, PolicyId, ModelHash));
            }
        }
        var first = Enumerable.Range(0, context.LegalActions.Candidates.Count)
            .First(index => context.LegalActions.Mask.Bits[index] == 1);
        var firstCandidate = context.LegalActions.Candidates[first];
        var choices = firstCandidate.SelectionConstraint is { } firstConstraint
            ? firstConstraint.ValidChoices.Take(firstConstraint.MinChoose).ToArray()
            : null;
        return ValueTask.FromResult(new AiPolicySelection(first, choices, PolicyId, ModelHash));
    }
}
