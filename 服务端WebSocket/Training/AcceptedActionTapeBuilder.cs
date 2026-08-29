using System.Text.Json;

namespace GrandUMI.Training;

public enum ReplayActionSource
{
    Player,
    System,
}

public sealed class AcceptedActionTapeEntry
{
    internal AcceptedActionTapeEntry(
        long orderSeq,
        long sourceSeq,
        long? resultSeq,
        int actorSeat,
        string action,
        JsonElement data,
        ReplayActionSource source,
        bool isTrainingLabelCandidate,
        string stableHash)
    {
        OrderSeq = orderSeq;
        SourceSeq = sourceSeq;
        ResultSeq = resultSeq;
        ActorSeat = actorSeat;
        Action = action;
        Data = data.Clone();
        Source = source;
        IsTrainingLabelCandidate = isTrainingLabelCandidate;
        StableHash = stableHash;
    }

    public long OrderSeq { get; }
    public long SourceSeq { get; }
    public long? ResultSeq { get; }
    public int ActorSeat { get; }
    public string Action { get; }
    public JsonElement Data { get; }
    public ReplayActionSource Source { get; }
    public bool IsTrainingLabelCandidate { get; }
    public string StableHash { get; }
}

public sealed record AcceptedActionTape(
    IReadOnlyList<AcceptedActionTapeEntry> Actions,
    string StableHash)
{
    public int HumanLabelCandidateCount => Actions.Count(action => action.IsTrainingLabelCandidate);
}

/// <summary>仅从 accepted 真人动作和已冻结系统事件构建确定性动作磁带。</summary>
public static class AcceptedActionTapeBuilder
{
    public static AcceptedActionTape Build(AdaptedMatchLog log, ReplayArtifactDescriptor artifact)
    {
        if (!IsSupportedAdapterVersion(artifact.EventAdapterVersion))
            throw Quarantine(
                ReplayQuarantineCodes.UnsupportedEventAdapter,
                $"当前 worker 不支持事件适配器 {artifact.EventAdapterVersion}",
                log.Header.MatchId);
        var requiresSelfContainedActions = string.Equals(
            artifact.EventAdapterVersion,
            MatchLogEventAdapter.CurrentAdapterVersion,
            StringComparison.Ordinal);

        var pendingPlayers = new List<PendingAction>();
        var pendingSystems = new List<PendingAction>();
        var promptResponses = log.Events
            .Where(e => string.Equals(e.Kind, "prompt_response", StringComparison.Ordinal))
            .ToDictionary(e => e.Seq);
        var consumedPromptResponses = new HashSet<long>();
        var seenRequestIds = new HashSet<string>(StringComparer.Ordinal);
        var actions = new List<AcceptedActionTapeEntry>();

        foreach (var entry in log.Events)
        {
            switch (entry.Kind)
            {
                case "player_action_requested":
                {
                    var pending = ParseRequest(
                        entry,
                        log.Header.MatchId,
                        requiresSelfContainedActions);
                    if (pending.RequestId is not null
                        && !seenRequestIds.Add($"{pending.ActorSeat}\n{pending.RequestId}"))
                        throw Quarantine(
                            ReplayQuarantineCodes.DuplicateRequestCorrelation,
                            $"同一席位重复使用 requestId：{pending.RequestId}",
                            log.Header.MatchId,
                            entry.Seq);
                    (pending.Source == ReplayActionSource.Player ? pendingPlayers : pendingSystems).Add(pending);
                    break;
                }
                case "player_action_accepted":
                    if (requiresSelfContainedActions)
                        ResolveSelfContainedAccepted(
                            entry,
                            pendingPlayers,
                            pendingSystems,
                            promptResponses,
                            consumedPromptResponses,
                            actions,
                            log.Header.MatchId);
                    else
                        ResolveAction(
                            entry,
                            accepted: true,
                            pendingPlayers,
                            pendingSystems,
                            promptResponses,
                            consumedPromptResponses,
                            actions,
                            log.Header.MatchId,
                            requireResultMetadata: false);
                    break;
                case "player_action_rejected":
                    ResolveAction(
                        entry,
                        accepted: false,
                        pendingPlayers,
                        pendingSystems,
                        promptResponses,
                        consumedPromptResponses,
                        actions,
                        log.Header.MatchId,
                        requireResultMetadata: requiresSelfContainedActions);
                    break;
                case "starting_player_choice_timeout_auto_select":
                    pendingSystems.Add(ParseStartingPlayerTimeout(
                        entry,
                        log.Header.MatchId,
                        requiresSelfContainedActions));
                    break;
                case "mulligan_timeout_auto_keep":
                    pendingSystems.Add(ParseMulliganTimeout(
                        entry,
                        log.Header.MatchId,
                        requiresSelfContainedActions));
                    break;
                case "prompt_timeout":
                    throw Quarantine(
                        ReplayQuarantineCodes.UnsupportedSystemEvent,
                        "旧 prompt_timeout 不含可唯一恢复的 chosen，不能静默推断",
                        log.Header.MatchId,
                        entry.Seq);
                default:
                    if (LooksLikeUnmappedSystemTransition(entry.Kind))
                        throw Quarantine(
                            ReplayQuarantineCodes.UnsupportedSystemEvent,
                            $"未登记的系统状态事件：{entry.Kind}",
                            log.Header.MatchId,
                            entry.Seq);
                    break;
            }
        }

        foreach (var pending in pendingSystems
                     .Where(item => item.CanApplyWithoutResult)
                     .OrderBy(item => item.SourceSeq)
                     .ToArray())
        {
            actions.Add(CreateTapeEntry(
                pending.SourceSeq,
                pending.SourceSeq,
                resultSeq: null,
                pending.ActorSeat,
                pending.Action,
                pending.Data,
                pending.Source,
                log.Header.MatchId));
            pendingSystems.Remove(pending);
        }

        if (pendingPlayers.Count > 0 || pendingSystems.Count > 0)
        {
            var pending = pendingPlayers.Concat(pendingSystems)
                .OrderBy(item => item.SourceSeq)
                .First();
            throw Quarantine(
                ReplayQuarantineCodes.UnresolvedAction,
                $"动作没有 accepted/rejected 结果：{pending.Action}",
                log.Header.MatchId,
                pending.SourceSeq);
        }

        var orphanPrompt = promptResponses.Keys
            .Where(seq => !consumedPromptResponses.Contains(seq))
            .OrderBy(seq => seq)
            .FirstOrDefault();
        if (orphanPrompt != 0)
            throw Quarantine(
                ReplayQuarantineCodes.OrphanPromptResponse,
                "prompt_response 无法关联到 accepted PromptResponse",
                log.Header.MatchId,
                orphanPrompt);

        var ordered = actions
            .OrderBy(action => action.OrderSeq)
            .ThenBy(action => action.SourceSeq)
            .ThenBy(action => action.ActorSeat)
            .ThenBy(action => action.Action, StringComparer.Ordinal)
            .ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i - 1].OrderSeq == ordered[i].OrderSeq)
                throw Quarantine(
                    ReplayQuarantineCodes.AmbiguousActionOrder,
                    $"多个动作共享应用序号 {ordered[i].OrderSeq}",
                    log.Header.MatchId,
                    ordered[i].OrderSeq);
        }

        var tapeHash = HashTape(ordered, log.Header.MatchId);
        return new AcceptedActionTape(Array.AsReadOnly(ordered), tapeHash);
    }

    private static PendingAction ParseRequest(
        AdaptedMatchLogEvent entry,
        string matchId,
        bool requireCorrelation)
    {
        var actor = RequireSeat(entry, matchId);
        var action = RequireAction(entry, matchId);
        if (!entry.Payload.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
            throw Quarantine(
                ReplayQuarantineCodes.MissingActionData,
                "player_action_requested 缺少对象 data",
                matchId,
                entry.Seq);
        EnsureCanonicalPayload(data, matchId, entry.Seq);
        var source = ReadSource(
            entry.Payload,
            defaultSource: requireCorrelation ? null : ReplayActionSource.Player,
            matchId,
            entry.Seq);
        return new PendingAction(
            actor,
            action,
            data.Clone(),
            source,
            entry.Seq,
            requireCorrelation
                ? ReadRequiredRequestId(entry.Payload, matchId, entry.Seq)
                : ReadOptionalRequestId(entry.Payload, matchId, entry.Seq),
            CanApplyWithoutResult: false);
    }

    private static PendingAction ParseStartingPlayerTimeout(
        AdaptedMatchLogEvent entry,
        string matchId,
        bool requireCorrelation)
    {
        var actor = RequireSeat(entry, matchId);
        if (!entry.Payload.TryGetProperty("goFirst", out var goFirst)
            || goFirst.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw Quarantine(
                ReplayQuarantineCodes.MalformedSystemEvent,
                "先后手超时事件缺少布尔 goFirst",
                matchId,
                entry.Seq);
        var data = JsonSerializer.SerializeToElement(new { goFirst = goFirst.GetBoolean() });
        return new PendingAction(
            actor,
            "ChooseFirstPlayer",
            data,
            ReplayActionSource.System,
            entry.Seq,
            requireCorrelation
                ? ReadRequiredRequestId(entry.Payload, matchId, entry.Seq)
                : ReadOptionalRequestId(entry.Payload, matchId, entry.Seq),
            CanApplyWithoutResult: false);
    }

    private static PendingAction ParseMulliganTimeout(
        AdaptedMatchLogEvent entry,
        string matchId,
        bool requireCorrelation)
    {
        var actor = RequireSeat(entry, matchId);
        if (!entry.Payload.TryGetProperty("redraw", out var redraw)
            || redraw.ValueKind != JsonValueKind.False)
            throw Quarantine(
                ReplayQuarantineCodes.MalformedSystemEvent,
                "调度超时自动保留必须明确记录 redraw=false",
                matchId,
                entry.Seq);
        var data = JsonSerializer.SerializeToElement(new { redraw = false });
        return new PendingAction(
            actor,
            "Mulligan",
            data,
            ReplayActionSource.System,
            entry.Seq,
            requireCorrelation
                ? ReadRequiredRequestId(entry.Payload, matchId, entry.Seq)
                : ReadOptionalRequestId(entry.Payload, matchId, entry.Seq),
            CanApplyWithoutResult: !requireCorrelation);
    }

    private static bool IsSupportedAdapterVersion(string version)
        => string.Equals(version, MatchLogEventAdapter.LegacyAdapterVersion, StringComparison.Ordinal)
            || string.Equals(version, MatchLogEventAdapter.CurrentAdapterVersion, StringComparison.Ordinal);

    private static void ResolveSelfContainedAccepted(
        AdaptedMatchLogEvent accepted,
        ICollection<PendingAction> pendingPlayers,
        ICollection<PendingAction> pendingSystems,
        IReadOnlyDictionary<long, AdaptedMatchLogEvent> promptResponses,
        ISet<long> consumedPromptResponses,
        ICollection<AcceptedActionTapeEntry> actions,
        string matchId)
    {
        var actor = RequireSeat(accepted, matchId);
        var action = RequireAction(accepted, matchId);
        var requestId = ReadRequiredRequestId(accepted.Payload, matchId, accepted.Seq);
        var source = ReadSource(accepted.Payload, defaultSource: null, matchId, accepted.Seq);
        if (!accepted.Payload.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
            throw Quarantine(
                ReplayQuarantineCodes.MissingActionData,
                "自包含 player_action_accepted 缺少对象 data",
                matchId,
                accepted.Seq);
        EnsureCanonicalPayload(data, matchId, accepted.Seq);

        var candidates = pendingPlayers.Concat(pendingSystems)
            .Where(candidate => candidate.ActorSeat == actor
                && candidate.Source == source
                && string.Equals(candidate.Action, action, StringComparison.Ordinal)
                && string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length > 1)
            throw Quarantine(
                ReplayQuarantineCodes.AmbiguousActionPairing,
                $"自包含 accepted 可关联到 {candidates.Length} 个候选：{action}",
                matchId,
                accepted.Seq);
        if (source == ReplayActionSource.Player && candidates.Length == 0)
            throw Quarantine(
                ReplayQuarantineCodes.OrphanActionResult,
                $"真人 accepted 缺少相同 requestId 的 requested：{action}",
                matchId,
                accepted.Seq);

        var pending = candidates.SingleOrDefault();
        if (pending is not null)
        {
            if (!string.Equals(CanonicalJson.Hash(pending.Data), CanonicalJson.Hash(data), StringComparison.Ordinal))
                throw Quarantine(
                    ReplayQuarantineCodes.AcceptedActionDataMismatch,
                    "accepted.data 与对应 requested.data 不一致",
                    matchId,
                    accepted.Seq);
            if (pending.Source == ReplayActionSource.Player)
                pendingPlayers.Remove(pending);
            else
                pendingSystems.Remove(pending);

            if (string.Equals(action, "PromptResponse", StringComparison.Ordinal))
                ValidatePromptResponse(
                    pending,
                    accepted,
                    promptResponses,
                    consumedPromptResponses,
                    matchId);
        }

        // v2 磁带的权威数据只依赖 accepted 自身；requested 仅作为相关性与篡改审计。
        actions.Add(CreateTapeEntry(
            accepted.Seq,
            accepted.Seq,
            accepted.Seq,
            actor,
            action,
            data,
            source,
            matchId));
    }

    private static void ResolveAction(
        AdaptedMatchLogEvent result,
        bool accepted,
        ICollection<PendingAction> pendingPlayers,
        ICollection<PendingAction> pendingSystems,
        IReadOnlyDictionary<long, AdaptedMatchLogEvent> promptResponses,
        ISet<long> consumedPromptResponses,
        ICollection<AcceptedActionTapeEntry> actions,
        string matchId,
        bool requireResultMetadata)
    {
        var actor = RequireSeat(result, matchId);
        var action = RequireAction(result, matchId);
        var requestId = requireResultMetadata
            ? ReadRequiredRequestId(result.Payload, matchId, result.Seq)
            : ReadOptionalRequestId(result.Payload, matchId, result.Seq);
        ReplayActionSource? resultSource = requireResultMetadata
            ? ReadSource(result.Payload, defaultSource: null, matchId, result.Seq)
            : result.Payload.TryGetProperty("source", out _)
                ? ReadSource(result.Payload, defaultSource: null, matchId, result.Seq)
                : null;
        var candidates = pendingPlayers
            .Concat(pendingSystems)
            .Where(candidate => candidate.ActorSeat == actor
                && string.Equals(candidate.Action, action, StringComparison.Ordinal)
                && (!resultSource.HasValue || candidate.Source == resultSource.Value)
                && (requireResultMetadata
                    ? string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal)
                    : requestId is null
                        || candidate.RequestId is null
                        || string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal)))
            .ToArray();
        if (candidates.Length == 0)
            throw Quarantine(
                ReplayQuarantineCodes.OrphanActionResult,
                $"{result.Kind} 没有可关联的 requested/system 事件：{action}",
                matchId,
                result.Seq);
        if (candidates.Length != 1)
            throw Quarantine(
                ReplayQuarantineCodes.AmbiguousActionPairing,
                $"{result.Kind} 可关联到 {candidates.Length} 个候选：{action}",
                matchId,
                result.Seq);

        var pending = candidates[0];
        if (pending.Source == ReplayActionSource.Player)
            pendingPlayers.Remove(pending);
        else
            pendingSystems.Remove(pending);

        if (!accepted)
        {
            if (pending.Source == ReplayActionSource.System)
                throw Quarantine(
                    ReplayQuarantineCodes.SystemActionRejected,
                    $"系统动作被引擎拒绝：{action}",
                    matchId,
                    result.Seq);
            return; // rejected 只参与质量审计，绝不进入磁带或训练标签。
        }

        if (string.Equals(action, "PromptResponse", StringComparison.Ordinal))
            ValidatePromptResponse(
                pending,
                result,
                promptResponses,
                consumedPromptResponses,
                matchId);

        actions.Add(CreateTapeEntry(
            result.Seq,
            pending.SourceSeq,
            result.Seq,
            pending.ActorSeat,
            pending.Action,
            pending.Data,
            pending.Source,
            matchId));
    }

    private static void ValidatePromptResponse(
        PendingAction pending,
        AdaptedMatchLogEvent accepted,
        IReadOnlyDictionary<long, AdaptedMatchLogEvent> promptResponses,
        ISet<long> consumedPromptResponses,
        string matchId)
    {
        var between = promptResponses.Values
            .Where(response => response.Seq > pending.SourceSeq && response.Seq < accepted.Seq)
            .ToArray();
        if (between.Length != 1)
            throw Quarantine(
                ReplayQuarantineCodes.PromptResponseMismatch,
                $"accepted PromptResponse 之间应恰有一个 prompt_response，实际 {between.Length}",
                matchId,
                accepted.Seq);
        var response = between[0];
        if (response.Actor != pending.ActorSeat)
            throw Quarantine(
                ReplayQuarantineCodes.PromptResponseMismatch,
                "prompt_response actor 与请求不一致",
                matchId,
                response.Seq);

        var requestPromptId = ReadPromptId(pending.Data, matchId, pending.SourceSeq);
        var responsePromptId = ReadPromptId(response.Payload, matchId, response.Seq);
        var requestChosen = ReadChosen(pending.Data, matchId, pending.SourceSeq);
        var responseChosen = ReadChosen(response.Payload, matchId, response.Seq);
        if (!string.Equals(requestPromptId, responsePromptId, StringComparison.Ordinal)
            || !requestChosen.SequenceEqual(responseChosen, StringComparer.Ordinal))
            throw Quarantine(
                ReplayQuarantineCodes.PromptResponseMismatch,
                "prompt_response 的 promptId/chosen 与 accepted 请求数据不一致",
                matchId,
                response.Seq);
        consumedPromptResponses.Add(response.Seq);
    }

    private static string ReadPromptId(JsonElement element, string matchId, long seq)
    {
        if (!element.TryGetProperty("promptId", out var promptId)
            || promptId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(promptId.GetString()))
            throw Quarantine(
                ReplayQuarantineCodes.PromptResponseMismatch,
                "PromptResponse 缺少 promptId",
                matchId,
                seq);
        return promptId.GetString()!;
    }

    private static string[] ReadChosen(JsonElement element, string matchId, long seq)
    {
        if (!element.TryGetProperty("chosen", out var chosen)
            || chosen.ValueKind != JsonValueKind.Array)
            throw Quarantine(
                ReplayQuarantineCodes.PromptResponseMismatch,
                "PromptResponse 缺少 chosen 数组",
                matchId,
                seq);
        var values = new List<string>();
        foreach (var item in chosen.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw Quarantine(
                    ReplayQuarantineCodes.PromptResponseMismatch,
                    "PromptResponse chosen 只能包含字符串",
                    matchId,
                    seq);
            values.Add(item.GetString()!);
        }
        return values.ToArray();
    }

    private static AcceptedActionTapeEntry CreateTapeEntry(
        long orderSeq,
        long sourceSeq,
        long? resultSeq,
        int actor,
        string action,
        JsonElement data,
        ReplayActionSource source,
        string matchId)
    {
        try
        {
            var canonical = AcceptedActionCanonicalizer.Create(
                orderSeq,
                sourceSeq,
                resultSeq,
                actor,
                action,
                data,
                source);
            return new AcceptedActionTapeEntry(
                canonical.OrderSeq,
                canonical.SourceSeq,
                canonical.ResultSeq,
                canonical.ActorSeat,
                canonical.Action,
                canonical.Data,
                canonical.Source,
                canonical.IsTrainingLabelCandidate,
                canonical.StableHash);
        }
        catch (InvalidDataException ex)
        {
            throw Quarantine(
                ReplayQuarantineCodes.NonCanonicalPayload,
                ex.Message,
                matchId,
                sourceSeq);
        }
    }

    private static string HashTape(IReadOnlyList<AcceptedActionTapeEntry> actions, string matchId)
    {
        try
        {
            var canonical = JsonSerializer.SerializeToElement(actions.Select(action => new
            {
                action.OrderSeq,
                action.SourceSeq,
                action.ResultSeq,
                action.ActorSeat,
                action.Action,
                action.Data,
                source = action.Source.ToString().ToLowerInvariant(),
                action.IsTrainingLabelCandidate,
                action.StableHash,
            }));
            return CanonicalJson.Hash(canonical);
        }
        catch (InvalidDataException ex)
        {
            throw Quarantine(
                ReplayQuarantineCodes.NonCanonicalPayload,
                ex.Message,
                matchId);
        }
    }

    private static void EnsureCanonicalPayload(JsonElement data, string matchId, long sourceSeq)
    {
        try
        {
            _ = CanonicalJson.Encode(data);
        }
        catch (InvalidDataException ex)
        {
            throw Quarantine(
                ReplayQuarantineCodes.NonCanonicalPayload,
                ex.Message,
                matchId,
                sourceSeq);
        }
    }

    private static ReplayActionSource ReadSource(
        JsonElement payload,
        ReplayActionSource? defaultSource,
        string matchId,
        long sourceSeq)
    {
        if (!payload.TryGetProperty("source", out var sourceElement))
        {
            if (defaultSource.HasValue) return defaultSource.Value;
            throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                "自包含动作缺少 source",
                matchId,
                sourceSeq);
        }
        if (sourceElement.ValueKind != JsonValueKind.String)
            throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                "动作 source 必须是 player 或 system",
                matchId,
                sourceSeq);
        return sourceElement.GetString() switch
        {
            "player" => ReplayActionSource.Player,
            "system" => ReplayActionSource.System,
            _ => throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                "动作 source 必须是 player 或 system",
                matchId,
                sourceSeq),
        };
    }

    private static string? ReadOptionalRequestId(JsonElement payload, string matchId, long sourceSeq)
    {
        if (!payload.TryGetProperty("requestId", out var requestIdElement)) return null;
        if (requestIdElement.ValueKind != JsonValueKind.String
            || requestIdElement.GetString() is not { Length: > 0 and <= 128 } requestId
            || !string.Equals(requestId, requestId.Trim(), StringComparison.Ordinal))
            throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                "requestId 必须是无首尾空白且不超过 128 字符的非空字符串",
                matchId,
                sourceSeq);
        return requestId;
    }

    private static string ReadRequiredRequestId(JsonElement payload, string matchId, long sourceSeq)
        => ReadOptionalRequestId(payload, matchId, sourceSeq)
            ?? throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                "自包含 accepted 缺少 requestId",
                matchId,
                sourceSeq);

    private static int RequireSeat(AdaptedMatchLogEvent entry, string matchId)
    {
        if (entry.Actor is not (0 or 1))
            throw Quarantine(
                ReplayQuarantineCodes.InvalidActor,
                $"{entry.Kind}.actor 必须是 0 或 1",
                matchId,
                entry.Seq);
        return entry.Actor.Value;
    }

    private static string RequireAction(AdaptedMatchLogEvent entry, string matchId)
    {
        if (!entry.Payload.TryGetProperty("action", out var actionElement)
            || actionElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(actionElement.GetString()))
            throw Quarantine(
                ReplayQuarantineCodes.MalformedActionResult,
                $"{entry.Kind} 缺少 action",
                matchId,
                entry.Seq);
        return actionElement.GetString()!;
    }

    private static bool LooksLikeUnmappedSystemTransition(string kind)
        => kind.Contains("_timeout_auto_", StringComparison.Ordinal)
            || kind.EndsWith("_timeout", StringComparison.Ordinal);

    private static ReplayQuarantineException Quarantine(
        string code,
        string message,
        string? matchId = null,
        long? sourceSeq = null)
        => new(code, "accepted_action_tape", message, matchId, sourceSeq);

    private sealed record PendingAction(
        int ActorSeat,
        string Action,
        JsonElement Data,
        ReplayActionSource Source,
        long SourceSeq,
        string? RequestId,
        bool CanApplyWithoutResult);
}
