using System.Text.Json;
using GrandUMI.Effects;
using GrandUMI.Game.Validation;
using GrandUMI.Training;

namespace GrandUMI.Game.Actions;

/// <summary>
/// 合法动作唯一枚举入口。候选全部重新走与 HandleAction 相同的纯校验；本类不得修改 GameState、RNG、日志或计时器。
/// </summary>
public static class LegalActionService
{
    private const int MaximumExpandedPromptChoices = 128;

    public static LegalActionSet Enumerate(
        GameState state,
        int actorSeat,
        LegalActionPurpose purpose = LegalActionPurpose.PlayerDecision)
    {
        ArgumentNullException.ThrowIfNull(state);
        var pending = new List<CandidateSeed>();
        if (actorSeat is < 0 or > 1 || state.IsGameOver)
            return Build(actorSeat, purpose, pending);

        // 平局协商、他人决策或效果链等待期间 fail closed；投降/平局属于行政动作，不进入策略空间。
        if (state.PendingDrawRequester is not null)
            return Build(actorSeat, purpose, pending);

        if (state.PendingPrompt is { } prompt)
        {
            if (prompt.PlayerIndex == actorSeat)
                AddPromptCandidates(state, actorSeat, purpose, prompt, pending);
            return Build(actorSeat, purpose, pending);
        }

        if (!state.StartingPlayerChosen)
        {
            AddConcrete(state, actorSeat, purpose, "ChooseFirstPlayer", new { goFirst = false }, "opening", pending);
            AddConcrete(state, actorSeat, purpose, "ChooseFirstPlayer", new { goFirst = true }, "opening", pending);
            return Build(actorSeat, purpose, pending);
        }

        if (!state.Players[actorSeat].MulliganDone)
        {
            AddConcrete(state, actorSeat, purpose, "Mulligan", new { redraw = false }, "opening", pending);
            if (state.Players[actorSeat].HasReDraw)
                AddConcrete(state, actorSeat, purpose, "Mulligan", new { redraw = true }, "opening", pending);
            return Build(actorSeat, purpose, pending);
        }

        if (!state.MulliganBothDone) return Build(actorSeat, purpose, pending);

        if (state.CurrentBattle is not null)
        {
            AddBattleCandidates(state, actorSeat, purpose, pending);
            return Build(actorSeat, purpose, pending);
        }

        if (state.CurrentTurnPlayer != actorSeat || state.Phase != Phase.Main)
            return Build(actorSeat, purpose, pending);

        AddMainPhaseCandidates(state, actorSeat, purpose, pending);
        return Build(actorSeat, purpose, pending);
    }

    /// <summary>与 HandleAction 共用的无副作用策略动作校验。</summary>
    public static ActionValidator.Result Validate(
        GameState state,
        int actorSeat,
        string action,
        JsonElement data)
    {
        if (actorSeat is < 0 or > 1) return new(false, "玩家席位非法");
        if (!LegalActionSpace.IsPolicyAction(action)) return new(false, "动作不属于策略动作空间");
        if (state.IsGameOver) return new(false, "对局已经结束");
        if (state.PendingDrawRequester is not null) return new(false, "请先处理当前平局申请");

        if (state.PendingPrompt is not null && action != "PromptResponse")
            return new(false, "当前有效果等待玩家处理");
        if (state.PendingPrompt is null && action == "PromptResponse")
            return new(false, "没有待响应的 prompt");
        if (!state.StartingPlayerChosen
            && action is not "ChooseFirstPlayer" and not "PromptResponse")
            return new(false, "请先完成先后手选择");

        try
        {
            return action switch
            {
                "ChooseFirstPlayer" => ValidateChooseFirstPlayer(state, actorSeat, data),
                "Mulligan" => ValidateMulligan(state, actorSeat, data),
                "PromptResponse" => ValidatePromptResponse(state, actorSeat, data),
                "PlayCard" => ValidatePlayCard(state, actorSeat, data),
                "AttachDon" => ValidateAttachDon(state, actorSeat, data),
                "Attack" => ValidateAttack(state, actorSeat, data),
                "UseEffect" => ValidateUseEffect(state, actorSeat, data),
                "DeclareBlocker" => ValidateDeclareBlocker(state, actorSeat, data),
                "PassBlock" => ActionValidator.CanPassBlock(state, actorSeat),
                "PlayCounter" => ValidatePlayCounter(state, actorSeat, data),
                "PassCounter" => ActionValidator.CanPassCounter(state, actorSeat),
                "EndTurn" => ActionValidator.CanEndTurn(state, actorSeat),
                _ => new(false, "动作不属于策略动作空间"),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return new(false, "动作参数格式非法");
        }
        catch (FormatException)
        {
            return new(false, "动作参数格式非法");
        }
        catch (OverflowException)
        {
            return new(false, "动作参数数值越界");
        }
    }

    /// <summary>把兼容 payload 投影到冻结语义字段，用于历史 accepted 覆盖与 actionId 匹配。</summary>
    public static bool TryCanonicalize(
        string action,
        JsonElement data,
        out JsonElement canonical,
        out string? reason)
    {
        canonical = default;
        reason = null;
        if (data.ValueKind != JsonValueKind.Object)
        {
            reason = "action_data_not_object";
            return false;
        }

        try
        {
            canonical = action switch
            {
                "ChooseFirstPlayer" => JsonSerializer.SerializeToElement(new
                {
                    goFirst = RequireBoolean(data, "goFirst"),
                }),
                "Mulligan" => JsonSerializer.SerializeToElement(new
                {
                    redraw = OptionalBoolean(data, "redraw", false),
                }),
                "PromptResponse" => JsonSerializer.SerializeToElement(new
                {
                    promptId = RequireString(data, "promptId"),
                    chosen = ReadStringArray(data, "chosen"),
                }),
                "PlayCard" => CanonicalizePlayCard(data),
                "AttachDon" => JsonSerializer.SerializeToElement(new
                {
                    targetId = RequireString(data, "targetId"),
                    count = OptionalInt32(data, "count", 1),
                }),
                "Attack" => CanonicalizeAttack(data),
                "UseEffect" => JsonSerializer.SerializeToElement(new
                {
                    sourceId = RequireString(data, "sourceId"),
                }),
                "DeclareBlocker" => JsonSerializer.SerializeToElement(new
                {
                    blockerId = RequireString(data, "blockerId"),
                }),
                "PassBlock" or "PassCounter" or "EndTurn" => JsonSerializer.SerializeToElement(new { }),
                "PlayCounter" => JsonSerializer.SerializeToElement(new
                {
                    handIndex = RequireInt32(data, "handIndex"),
                    useCounterIcon = OptionalBoolean(data, "useCounterIcon", false),
                }),
                _ => throw new InvalidDataException("unsupported_action"),
            };
            canonical = CanonicalJson.NormalizeObject(canonical);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or OverflowException)
        {
            reason = "action_data_malformed";
            canonical = default;
            return false;
        }
    }

    public static bool Contains(
        LegalActionSet set,
        string action,
        JsonElement data,
        out string? actionId,
        out string? reason)
    {
        actionId = null;
        reason = null;
        if (!TryCanonicalize(action, data, out var canonical, out reason)) return false;
        var canonicalHash = CanonicalJson.Hash(canonical);
        foreach (var candidate in set.Candidates)
        {
            if (!string.Equals(candidate.Action, action, StringComparison.Ordinal)) continue;
            if (candidate.SelectionConstraint is { } constraint)
            {
                var promptId = canonical.GetProperty("promptId").GetString() ?? string.Empty;
                var chosen = canonical.GetProperty("chosen").EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray();
                if (!string.Equals(promptId, constraint.PromptId, StringComparison.Ordinal)
                    || !Satisfies(constraint, chosen, out _))
                    continue;
                actionId = candidate.ActionId;
                return true;
            }

            if (!string.Equals(CanonicalJson.Hash(candidate.Data), canonicalHash, StringComparison.Ordinal)) continue;
            actionId = candidate.ActionId;
            return true;
        }
        reason = "accepted_action_not_in_legal_set";
        return false;
    }

    internal static bool Satisfies(
        LegalSelectionConstraint constraint,
        IReadOnlyList<string> chosen,
        out string? reason)
    {
        reason = null;
        if (chosen.Count < constraint.MinChoose || chosen.Count > constraint.MaxChoose)
        {
            reason = "prompt_choice_count_out_of_range";
            return false;
        }
        if (constraint.Unique && chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Count)
        {
            reason = "prompt_choice_duplicate";
            return false;
        }
        var valid = constraint.ValidChoices.ToHashSet(StringComparer.Ordinal);
        if (chosen.Any(choice => !valid.Contains(choice)))
        {
            reason = "prompt_choice_not_allowed";
            return false;
        }
        return true;
    }

    private static void AddMainPhaseCandidates(
        GameState state,
        int actor,
        LegalActionPurpose purpose,
        ICollection<CandidateSeed> pending)
    {
        var me = state.Players[actor];
        for (var handIndex = 0; handIndex < me.Hand.Count; handIndex++)
        {
            AddConcrete(state, actor, purpose, "PlayCard", new { handIndex }, "main", pending);
            if (me.Hand[handIndex].Info.Kind == Cards.CardKind.Character && me.Characters.Count >= 5)
            {
                foreach (var victim in me.Characters.OrderBy(card => card.Id))
                    AddConcrete(state, actor, purpose, "PlayCard", new
                    {
                        handIndex,
                        overflowTrashCardId = victim.Id.ToString(),
                    }, "main", pending);
            }
        }

        if (me.ActiveDonCount > 0)
        {
            var targets = new[] { "leader" }
                .Concat(me.Characters.OrderBy(card => card.Id).Select(card => card.Id.ToString()));
            foreach (var target in targets)
                for (var count = 1; count <= me.ActiveDonCount; count++)
                    AddConcrete(state, actor, purpose, "AttachDon", new { targetId = target, count }, "main", pending);
        }

        var attackers = new[] { me.Leader }.Concat(me.Characters).OrderBy(card => card.Id).ToArray();
        foreach (var attacker in attackers)
        {
            AddConcrete(state, actor, purpose, "Attack", new
            {
                attackerId = attacker.Id.ToString(),
                targetIsLeader = true,
            }, "combat", pending);
            foreach (var target in state.Players[1 - actor].Characters.OrderBy(card => card.Id))
                AddConcrete(state, actor, purpose, "Attack", new
                {
                    attackerId = attacker.Id.ToString(),
                    targetIsLeader = false,
                    targetId = target.Id.ToString(),
                }, "combat", pending);
        }

        var effectSources = new[] { me.Leader }
            .Concat(me.Characters)
            .Concat(me.StageCard is null ? Array.Empty<CardInstance>() : new[] { me.StageCard })
            .OrderBy(card => card.Id);
        foreach (var source in effectSources)
            AddConcrete(state, actor, purpose, "UseEffect", new { sourceId = source.Id.ToString() }, "main", pending);

        AddConcrete(state, actor, purpose, "EndTurn", new { }, "main", pending);
    }

    private static void AddBattleCandidates(
        GameState state,
        int actor,
        LegalActionPurpose purpose,
        ICollection<CandidateSeed> pending)
    {
        var me = state.Players[actor];
        if (state.Phase == Phase.BattleBlock)
        {
            foreach (var blocker in me.Characters.OrderBy(card => card.Id))
                AddConcrete(state, actor, purpose, "DeclareBlocker", new
                {
                    blockerId = blocker.Id.ToString(),
                }, "combat", pending);
            AddConcrete(state, actor, purpose, "PassBlock", new { }, "combat", pending);
            return;
        }

        if (state.Phase != Phase.BattleCounter) return;
        for (var handIndex = 0; handIndex < me.Hand.Count; handIndex++)
        {
            AddConcrete(state, actor, purpose, "PlayCounter", new
            {
                handIndex,
                useCounterIcon = true,
            }, "combat", pending);
            AddConcrete(state, actor, purpose, "PlayCounter", new
            {
                handIndex,
                useCounterIcon = false,
            }, "combat", pending);
        }
        AddConcrete(state, actor, purpose, "PassCounter", new { }, "combat", pending);
    }

    private static void AddPromptCandidates(
        GameState state,
        int actor,
        LegalActionPurpose purpose,
        PendingPrompt prompt,
        ICollection<CandidateSeed> pending)
    {
        var choices = prompt.ValidChoices.Distinct(StringComparer.Ordinal).ToArray();
        if (prompt.MinChoose == prompt.MaxChoose && prompt.MinChoose == 0)
        {
            AddConcrete(state, actor, purpose, "PromptResponse", new
            {
                promptId = prompt.PromptId,
                chosen = Array.Empty<string>(),
            }, "prompt", pending);
            return;
        }

        if (prompt.MaxChoose <= 1 && choices.Length <= MaximumExpandedPromptChoices)
        {
            if (prompt.MinChoose == 0)
                AddConcrete(state, actor, purpose, "PromptResponse", new
                {
                    promptId = prompt.PromptId,
                    chosen = Array.Empty<string>(),
                }, "prompt", pending);
            foreach (var choice in choices)
                AddConcrete(state, actor, purpose, "PromptResponse", new
                {
                    promptId = prompt.PromptId,
                    chosen = new[] { choice },
                }, "prompt", pending);
            return;
        }

        var ordered = prompt.Kind.Contains("Order", StringComparison.OrdinalIgnoreCase)
            || prompt.Kind.Contains("Reorder", StringComparison.OrdinalIgnoreCase)
            || prompt.Kind.Contains("Scry", StringComparison.OrdinalIgnoreCase);
        var constraint = new LegalSelectionConstraint(
            prompt.PromptId,
            prompt.Kind,
            Array.AsReadOnly(choices),
            prompt.MinChoose,
            prompt.MaxChoose,
            ordered,
            Unique: true);
        var template = JsonSerializer.SerializeToElement(new { promptId = prompt.PromptId });
        pending.Add(new CandidateSeed(
            "PromptResponse",
            CanonicalJson.NormalizeObject(template),
            "prompt",
            IsTrainingEligible(purpose),
            constraint));
    }

    private static void AddConcrete(
        GameState state,
        int actor,
        LegalActionPurpose purpose,
        string action,
        object data,
        string category,
        ICollection<CandidateSeed> pending)
    {
        var element = JsonSerializer.SerializeToElement(data);
        if (!TryCanonicalize(action, element, out var canonical, out _)) return;
        if (!Validate(state, actor, action, canonical).Ok) return;
        pending.Add(new CandidateSeed(
            action,
            canonical,
            category,
            IsTrainingEligible(purpose),
            SelectionConstraint: null));
    }

    private static LegalActionSet Build(
        int actor,
        LegalActionPurpose purpose,
        IEnumerable<CandidateSeed> pending)
    {
        var candidates = pending
            .Select(seed => new
            {
                Seed = seed,
                SortData = CanonicalJson.Encode(seed.Data),
                ConstraintHash = seed.SelectionConstraint is null
                    ? string.Empty
                    : CanonicalJson.Hash(JsonSerializer.SerializeToElement(seed.SelectionConstraint)),
            })
            .OrderBy(item => LegalActionSpace.OrderOf(item.Seed.Action))
            .ThenBy(item => Convert.ToHexString(item.SortData), StringComparer.Ordinal)
            .ThenBy(item => item.ConstraintHash, StringComparer.Ordinal)
            .Select(item =>
            {
                var identity = JsonSerializer.SerializeToElement(new
                {
                    schema = LegalActionSpace.Schema,
                    action = item.Seed.Action,
                    data = item.Seed.Data,
                    selectionConstraint = item.Seed.SelectionConstraint,
                });
                return new LegalActionCandidate(
                    CanonicalJson.Hash(identity),
                    item.Seed.Action,
                    item.Seed.Data.Clone(),
                    item.Seed.Category,
                    item.Seed.IsTrainingEligible,
                    item.Seed.SelectionConstraint);
            })
            .GroupBy(candidate => candidate.ActionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var bits = Enumerable.Repeat(1, candidates.Length).ToArray();
        var maskHash = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            schema = LegalActionSpace.MaskSchema,
            bits,
        }));
        var mask = new LegalActionMask(
            LegalActionSpace.MaskSchema,
            Array.AsReadOnly(bits),
            maskHash);
        var setHash = CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            schema = LegalActionSpace.SetSchema,
            actionSpaceHash = LegalActionSpace.ActionSpaceHash,
            actorSeat = actor,
            purpose = purpose.ToString(),
            actionIds = candidates.Select(candidate => candidate.ActionId).ToArray(),
            maskHash,
        }));
        return new LegalActionSet(
            LegalActionSpace.SetSchema,
            LegalActionSpace.ActionSpaceHash,
            actor,
            purpose,
            Array.AsReadOnly(candidates),
            mask,
            setHash);
    }

    private static bool IsTrainingEligible(LegalActionPurpose purpose)
        => purpose is LegalActionPurpose.PlayerDecision or LegalActionPurpose.Training;

    private static ActionValidator.Result ValidateChooseFirstPlayer(GameState state, int actor, JsonElement data)
        => ActionValidator.CanChooseFirstPlayer(state, actor, RequireBoolean(data, "goFirst"));

    private static ActionValidator.Result ValidateMulligan(GameState state, int actor, JsonElement data)
        => ActionValidator.CanMulligan(state, actor, OptionalBoolean(data, "redraw", false));

    private static ActionValidator.Result ValidatePromptResponse(GameState state, int actor, JsonElement data)
        => ActionValidator.CanRespondPrompt(
            state,
            actor,
            RequireString(data, "promptId"),
            ReadStringArray(data, "chosen"));

    private static ActionValidator.Result ValidatePlayCard(GameState state, int actor, JsonElement data)
    {
        var handIndex = RequireInt32(data, "handIndex");
        var baseResult = ActionValidator.CanPlayCard(state, actor, handIndex);
        if (!baseResult.Ok) return baseResult;
        if (!data.TryGetProperty("overflowTrashCardId", out var victim)) return baseResult;
        if (victim.ValueKind != JsonValueKind.String
            || !Guid.TryParse(victim.GetString(), out var victimId)
            || !state.Players[actor].Characters.Any(card => card.Id == victimId))
            return new(false, "腾位角色 ID 无效");
        var hand = state.Players[actor].Hand;
        if (handIndex < 0 || handIndex >= hand.Count
            || hand[handIndex].Info.Kind != Cards.CardKind.Character
            || state.Players[actor].Characters.Count < 5)
            return new(false, "当前出牌不需要腾位角色");
        return baseResult;
    }

    private static ActionValidator.Result ValidateAttachDon(GameState state, int actor, JsonElement data)
        => ActionValidator.CanAttachDon(
            state,
            actor,
            RequireString(data, "targetId"),
            OptionalInt32(data, "count", 1));

    private static ActionValidator.Result ValidateAttack(GameState state, int actor, JsonElement data)
    {
        if (!Guid.TryParse(RequireString(data, "attackerId"), out var attackerId))
            return new(false, "攻击者 ID 非法");
        var targetIsLeader = RequireBoolean(data, "targetIsLeader");
        Guid? targetId = null;
        if (!targetIsLeader)
        {
            if (!data.TryGetProperty("targetId", out var target)
                || target.ValueKind != JsonValueKind.String
                || !Guid.TryParse(target.GetString(), out var parsed))
                return new(false, "目标 ID 非法");
            targetId = parsed;
        }
        return ActionValidator.CanAttack(state, actor, attackerId, targetIsLeader, targetId);
    }

    private static ActionValidator.Result ValidateUseEffect(GameState state, int actor, JsonElement data)
    {
        if (!Guid.TryParse(RequireString(data, "sourceId"), out var sourceId))
            return new(false, "sourceId 非法");
        return ActionValidator.CanUseEffect(state, actor, sourceId);
    }

    private static ActionValidator.Result ValidateDeclareBlocker(GameState state, int actor, JsonElement data)
    {
        if (!Guid.TryParse(RequireString(data, "blockerId"), out var blockerId))
            return new(false, "blockerId 非法");
        return ActionValidator.CanDeclareBlocker(state, actor, blockerId);
    }

    private static ActionValidator.Result ValidatePlayCounter(GameState state, int actor, JsonElement data)
        => ActionValidator.CanPlayCounter(
            state,
            actor,
            RequireInt32(data, "handIndex"),
            OptionalBoolean(data, "useCounterIcon", false));

    private static JsonElement CanonicalizePlayCard(JsonElement data)
    {
        var handIndex = RequireInt32(data, "handIndex");
        if (!data.TryGetProperty("overflowTrashCardId", out var victim))
            return JsonSerializer.SerializeToElement(new { handIndex });
        if (victim.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("overflowTrashCardId 必须是字符串");
        return JsonSerializer.SerializeToElement(new
        {
            handIndex,
            overflowTrashCardId = victim.GetString() ?? string.Empty,
        });
    }

    private static JsonElement CanonicalizeAttack(JsonElement data)
    {
        var attackerId = RequireString(data, "attackerId");
        var targetIsLeader = RequireBoolean(data, "targetIsLeader");
        if (targetIsLeader)
            return JsonSerializer.SerializeToElement(new { attackerId, targetIsLeader = true });
        return JsonSerializer.SerializeToElement(new
        {
            attackerId,
            targetIsLeader = false,
            targetId = RequireString(data, "targetId"),
        });
    }

    private static string RequireString(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"缺少字符串字段 {name}");
        return value.GetString() ?? string.Empty;
    }

    private static bool RequireBoolean(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value)
            || value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidDataException($"缺少布尔字段 {name}");
        return value.GetBoolean();
    }

    private static bool OptionalBoolean(JsonElement data, string name, bool fallback)
    {
        if (!data.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidDataException($"字段 {name} 必须是布尔值");
        return value.GetBoolean();
    }

    private static int RequireInt32(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"缺少整数 {name}");
        return result;
    }

    private static int OptionalInt32(JsonElement data, string name, int fallback)
    {
        if (!data.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"字段 {name} 必须是整数");
        return result;
    }

    private static string[] ReadStringArray(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value)) return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"字段 {name} 必须是数组");
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"字段 {name} 只能包含字符串");
            result.Add(item.GetString() ?? string.Empty);
        }
        return result.ToArray();
    }

    private sealed record CandidateSeed(
        string Action,
        JsonElement Data,
        string Category,
        bool IsTrainingEligible,
        LegalSelectionConstraint? SelectionConstraint);
}
