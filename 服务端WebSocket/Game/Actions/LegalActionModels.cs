using System.Text.Json;
using GrandUMI.Training;

namespace GrandUMI.Game.Actions;

/// <summary>合法动作集合的消费场景。系统动作始终不具备真人训练标签资格。</summary>
public enum LegalActionPurpose
{
    PlayerDecision,
    Training,
    Inference,
    System,
    Replay,
}

/// <summary>
/// 组合数量可能爆炸的 Prompt 选择约束。候选本身是参数化动作，执行前必须物化 chosen 并再次校验。
/// </summary>
public sealed record LegalSelectionConstraint(
    string PromptId,
    string PromptKind,
    IReadOnlyList<string> ValidChoices,
    int MinChoose,
    int MaxChoose,
    bool Ordered,
    bool Unique);

/// <summary>一个已校验的具体动作，或一个带选择约束的参数化 Prompt 动作。</summary>
public sealed record LegalActionCandidate(
    string ActionId,
    string Action,
    JsonElement Data,
    string Category,
    bool IsTrainingLabelEligible,
    LegalSelectionConstraint? SelectionConstraint)
{
    public bool IsParameterized => SelectionConstraint is not null;
}

/// <summary>候选级 mask。当前集合只存合法项，因此每一位必须为 1；显式保留便于模型接口冻结。</summary>
public sealed record LegalActionMask(
    string Schema,
    IReadOnlyList<int> Bits,
    string StableHash);

/// <summary>可审计、可稳定序列化的合法动作集合。</summary>
public sealed record LegalActionSet(
    string Schema,
    string ActionSpaceHash,
    int ActorSeat,
    LegalActionPurpose Purpose,
    IReadOnlyList<LegalActionCandidate> Candidates,
    LegalActionMask Mask,
    string StableHash)
{
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>
    /// 物化模型选择。具体候选不接受额外 choices；参数化候选必须满足冻结约束。
    /// 物化结果仍需由 LegalActionService.Validate 复核，调用方不得直接发给引擎。
    /// </summary>
    public bool TryMaterialize(
        int candidateIndex,
        IReadOnlyList<string>? selectedChoices,
        out string action,
        out JsonElement data,
        out string? reason)
    {
        action = string.Empty;
        data = default;
        reason = null;
        if (candidateIndex < 0 || candidateIndex >= Candidates.Count)
        {
            reason = "candidate_index_out_of_range";
            return false;
        }

        var candidate = Candidates[candidateIndex];
        action = candidate.Action;
        if (candidate.SelectionConstraint is not { } constraint)
        {
            if (selectedChoices is { Count: > 0 })
            {
                reason = "unexpected_parameterized_choices";
                return false;
            }
            data = candidate.Data.Clone();
            return true;
        }

        var chosen = selectedChoices?.ToArray() ?? Array.Empty<string>();
        if (!LegalActionService.Satisfies(constraint, chosen, out reason)) return false;
        data = JsonSerializer.SerializeToElement(new
        {
            promptId = constraint.PromptId,
            chosen,
        });
        return true;
    }
}

/// <summary>动作空间冻结描述；任何字段变化都会改变 ActionSpaceHash。</summary>
public static class LegalActionSpace
{
    public const string Schema = "grandumi.action.v1";
    public const string SetSchema = "grandumi.legal_action_set.v1";
    public const string MaskSchema = "grandumi.legal_action_mask.v1";

    private static readonly string[] OrderedActions =
    [
        "ChooseFirstPlayer",
        "Mulligan",
        "PromptResponse",
        "PlayCard",
        "AttachDon",
        "Attack",
        "UseEffect",
        "DeclareBlocker",
        "PassBlock",
        "PlayCounter",
        "PassCounter",
        "EndTurn",
    ];

    private static readonly IReadOnlyDictionary<string, int> ActionOrder = OrderedActions
        .Select((action, index) => (action, index))
        .ToDictionary(item => item.action, item => item.index, StringComparer.Ordinal);

    public static string ActionSpaceHash { get; } = CanonicalJson.Hash(
        JsonSerializer.SerializeToElement(new
        {
            schema = Schema,
            actions = new object[]
            {
                new { name = "ChooseFirstPlayer", data = new[] { "goFirst:boolean" } },
                new { name = "Mulligan", data = new[] { "redraw:boolean" } },
                new { name = "PromptResponse", data = new[] { "promptId:string", "chosen:string[]" }, parameterized = true },
                new { name = "PlayCard", data = new[] { "handIndex:int", "overflowTrashCardId?:guid" } },
                new { name = "AttachDon", data = new[] { "targetId:leader|guid", "count:int" } },
                new { name = "Attack", data = new[] { "attackerId:guid", "targetIsLeader:boolean", "targetId?:guid" } },
                new { name = "UseEffect", data = new[] { "sourceId:guid" } },
                new { name = "DeclareBlocker", data = new[] { "blockerId:guid" } },
                new { name = "PassBlock", data = Array.Empty<string>() },
                new { name = "PlayCounter", data = new[] { "handIndex:int", "useCounterIcon:boolean" } },
                new { name = "PassCounter", data = Array.Empty<string>() },
                new { name = "EndTurn", data = Array.Empty<string>() },
            },
        }));

    internal static int OrderOf(string action)
        => ActionOrder.TryGetValue(action, out var order) ? order : int.MaxValue;

    public static bool IsPolicyAction(string action)
        => ActionOrder.ContainsKey(action);
}
