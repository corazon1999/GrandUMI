using System.Text.Json;
using GrandUMI.Game.Actions;
using GrandUMI.Training;

namespace GrandUMI.Game.AI;

public sealed record AiPolicyContext(
    TrainingObservation Observation,
    LegalActionSet LegalActions);

public sealed record AiPolicySelection(
    int CandidateIndex,
    IReadOnlyList<string>? SelectedChoices,
    string PolicyId,
    string ModelHash);

public sealed record AiResolvedDecision(
    string Action,
    JsonElement Data,
    string ActionId,
    string PolicyId,
    string ModelHash,
    bool UsedFallback,
    string? FallbackReason);

public interface IAiPolicy
{
    string PolicyId { get; }
    string ModelHash { get; }

    ValueTask<AiPolicySelection> SelectAsync(
        AiPolicyContext context,
        CancellationToken cancellationToken);
}

/// <summary>推理故障边界：超时、异常、越界、越 mask 或物化后不合法都切到确定性安全策略。</summary>
public static class AiDecisionCoordinator
{
    public static async Task<AiResolvedDecision?> DecideAsync(
        GameState state,
        int actorSeat,
        IAiPolicy primary,
        IAiPolicy fallback,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var legal = LegalActionService.Enumerate(state, actorSeat, LegalActionPurpose.Inference);
        if (legal.IsEmpty) return null;
        var observation = TrainingObservationBuilder.Build(state, actorSeat);
        var context = new AiPolicyContext(observation, legal);

        string? fallbackReason = null;
        AiPolicySelection? selection = null;
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout);
            try
            {
                selection = await primary.SelectAsync(context, timeoutSource.Token);
                if (!TryResolve(state, actorSeat, legal, selection, out var resolved, out fallbackReason))
                    selection = null;
                else
                    return resolved;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                fallbackReason = "model_timeout";
            }
            catch (Exception)
            {
                fallbackReason = "model_exception";
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            selection = await fallback.SelectAsync(context, cancellationToken);
            if (!TryResolve(state, actorSeat, legal, selection, out var fallbackDecision, out var fallbackValidation))
                return null;
            return fallbackDecision! with
            {
                UsedFallback = true,
                FallbackReason = fallbackReason ?? fallbackValidation ?? "model_invalid_selection",
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool TryResolve(
        GameState state,
        int actorSeat,
        LegalActionSet legal,
        AiPolicySelection selection,
        out AiResolvedDecision? decision,
        out string? reason)
    {
        decision = null;
        reason = null;
        if (selection.CandidateIndex < 0 || selection.CandidateIndex >= legal.Candidates.Count)
        {
            reason = "model_candidate_index_out_of_range";
            return false;
        }
        if (selection.CandidateIndex >= legal.Mask.Bits.Count
            || legal.Mask.Bits[selection.CandidateIndex] != 1)
        {
            reason = "model_candidate_masked";
            return false;
        }
        if (!legal.TryMaterialize(
                selection.CandidateIndex,
                selection.SelectedChoices,
                out var action,
                out var data,
                out reason))
            return false;
        var validation = LegalActionService.Validate(state, actorSeat, action, data);
        if (!validation.Ok)
        {
            reason = "materialized_action_rejected";
            return false;
        }
        decision = new AiResolvedDecision(
            action,
            data,
            legal.Candidates[selection.CandidateIndex].ActionId,
            selection.PolicyId,
            selection.ModelHash,
            UsedFallback: false,
            FallbackReason: null);
        return true;
    }
}
