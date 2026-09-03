using GrandUMI.Cards;
using GrandUMI.Effects;

namespace GrandUMI.Game.PhaseFlow;

/// <summary>
/// 战斗 5 步骤：Attack → Block → Counter → Damage → BattleEnd
///
/// 异步版本：所有可能触发效果的操作走 async，以便 EffectRuntime/Prompt 串接玩家选择。
/// </summary>
public static class BattleEngine
{
    /// <summary>
    /// 攻击宣言（同步部分）：横置攻击者 + 写入 BattleContext + 进入 BattleAttack 阶段
    /// 触发【攻击时】/【对方的攻击时】由调用者通过 TriggerAttackDeclareAsync 异步处理
    /// </summary>
    public static void StartAttack(GameState s, Guid attackerId, bool targetIsLeader, Guid? targetId)
    {
        var atkPlayer = s.CurrentTurnPlayer;
        var defPlayer = 1 - atkPlayer;

        var me = s.Players[atkPlayer];
        CardInstance attacker = (me.Leader.Id == attackerId) ? me.Leader
            : me.Characters.First(c => c.Id == attackerId);
        if (!AtomicOps.CanRestCard(s, attacker, atkPlayer))
            throw new InvalidOperationException("攻击者无法转为休息状态，不能宣言攻击");
        attacker.IsTapped = true;

        s.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = atkPlayer,
            DefenderPlayerIndex = defPlayer,
            AttackerCardId = attackerId,
            TargetIsLeader = targetIsLeader,
            TargetCardId = targetIsLeader ? null : targetId,
        };
        s.Phase = Phase.BattleAttack;
    }

    /// <summary>GM 调试：不依赖当前回合玩家，强制由 attackerIdx 发起攻击（其余流程与正常攻击一致）。</summary>
    public static void StartAttackForced(GameState s, int attackerIdx, Guid attackerId, bool targetIsLeader, Guid? targetId)
    {
        var me = s.Players[attackerIdx];
        CardInstance attacker = (me.Leader.Id == attackerId) ? me.Leader
            : me.Characters.First(c => c.Id == attackerId);
        if (!AtomicOps.CanRestCard(s, attacker, attackerIdx))
            throw new InvalidOperationException("攻击者无法转为休息状态，不能宣言攻击");
        attacker.IsTapped = true;

        s.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = attackerIdx,
            DefenderPlayerIndex = 1 - attackerIdx,
            AttackerCardId = attackerId,
            TargetIsLeader = targetIsLeader,
            TargetCardId = targetIsLeader ? null : targetId,
        };
        s.Phase = Phase.BattleAttack;
    }

    /// <summary>触发【攻击时】+【对方的攻击时】效果（按回合玩家优先顺序），完成后进入 BattleBlock</summary>
    public static async Task TriggerAttackDeclareAsync(GameState s, IPromptService prompts)
    {
        // 攻击宣言横置也属"转为休息状态"（反馈#227 OP14-119）：对角色攻击者派发 OnCharRested(reason=attack)。
        // "因效果转休息"类监听卡已按 reason 过滤，不受影响。
        if (s.CurrentBattle is { } ab)
        {
            var atkP = s.Players[ab.AttackerPlayerIndex];
            var atkChar = atkP.Characters.FirstOrDefault(c => c.Id == ab.AttackerCardId);
            if (atkChar is not null)
            {
                await EffectRuntime.TriggerEvent(s, EffectTrigger.OnCharRested, prompts,
                    new Dictionary<string, object?> { ["restedCardId"] = atkChar.Id.ToString(), ["reason"] = "attack" });
                if (s.IsGameOver) return;
            }
        }
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnAttackDeclare, prompts);
        if (s.IsGameOver) return;
        if (s.CurrentBattle is { } leaderBattle)
        {
            var attackerSide = s.Players[leaderBattle.AttackerPlayerIndex];
            bool attackerIsLeader = attackerSide.Leader.Id == leaderBattle.AttackerCardId;
            if (attackerIsLeader || leaderBattle.TargetIsLeader)
            {
                await EffectRuntime.TriggerEvent(s, EffectTrigger.OnLeaderBattle, prompts,
                    new Dictionary<string, object?>
                    {
                        ["attackerId"] = leaderBattle.AttackerCardId.ToString(),
                        ["attackerOwner"] = leaderBattle.AttackerPlayerIndex,
                        ["targetLeaderOwner"] = leaderBattle.TargetIsLeader ? leaderBattle.DefenderPlayerIndex : -1,
                    });
                if (s.IsGameOver) return;
            }
        }
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnOppAttackDeclare, prompts,
            new Dictionary<string, object?> { ["AttackerIdx"] = s.CurrentBattle!.AttackerPlayerIndex });
        if (s.IsGameOver) return;
        s.Phase = Phase.BattleBlock;
    }

    /// <summary>防守方宣言阻挡者（同步） + 触发【阻挡时】（异步）</summary>
    public static void DeclareBlocker(GameState s, Guid blockerId)
    {
        var b = s.CurrentBattle!;
        var defender = s.Players[b.DefenderPlayerIndex];
        var blocker = defender.Characters.First(c => c.Id == blockerId);
        if (!AtomicOps.CanRestCard(s, blocker, b.DefenderPlayerIndex))
            throw new InvalidOperationException("阻挡者无法转为休息状态，不能宣言阻挡");
        b.BlockerDeclared = true;
        b.ReplacedByBlockerCardId = blockerId;
        blocker.IsTapped = true;
        b.TargetIsLeader = false;
        b.TargetCardId = blockerId;
        s.Phase = Phase.BattleCounter;
    }

    public static async Task TriggerBlockDeclareAsync(GameState s, IPromptService prompts)
    {
        // 阻挡宣言横置同属"转为休息状态"（reason=block），"因效果"类监听卡按 reason 过滤不受影响
        if (s.CurrentBattle is { ReplacedByBlockerCardId: { } bid })
        {
            await EffectRuntime.TriggerEvent(s, EffectTrigger.OnCharRested, prompts,
                new Dictionary<string, object?> { ["restedCardId"] = bid.ToString(), ["reason"] = "block" });
            if (s.IsGameOver) return;
        }
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnBlockDeclare, prompts,
            new Dictionary<string, object?>
            {
                ["blockerCardId"] = s.CurrentBattle?.ReplacedByBlockerCardId?.ToString(),
                ["owner"] = s.CurrentBattle?.DefenderPlayerIndex,
            });
    }

    public static void PassBlock(GameState s)
    {
        s.Phase = Phase.BattleCounter;
    }

    public static void ApplyCounter(GameState s, int defenderIdx, int counterValue)
    {
        var b = s.CurrentBattle!;
        var def = s.Players[b.DefenderPlayerIndex];
        // 反击值加到"当前被攻击目标"卡上的【本次战斗】力量修正，使卡面战力同步显示；
        // EndBattle 会清除 PowerModThisBattle，即实现"直到战斗结束"后自动移除。
        CardInstance? target = b.TargetIsLeader
            ? def.Leader
            : def.Characters.FirstOrDefault(c => c.Id == b.TargetCardId);
        if (target is null) return;
        target.PowerModThisBattle += counterValue;
    }

    /// <summary>防守方放弃反击 → 进入伤害步骤</summary>
    public static void PassCounter(GameState s)
    {
        s.Phase = Phase.BattleDamage;
    }

    /// <summary>
    /// Returns whether the attacking card and the current attack target are still on the field.
    /// At a battle step boundary, the battle ends if either participating Character moved away.
    /// </summary>
    public static bool AreBattleParticipantsOnField(GameState s)
    {
        if (s.CurrentBattle is not { } b) return false;

        var attackerSide = s.Players[b.AttackerPlayerIndex];
        bool attackerPresent = attackerSide.Leader.Id == b.AttackerCardId
            || attackerSide.Characters.Any(card => card.Id == b.AttackerCardId);
        if (!attackerPresent) return false;

        if (b.TargetIsLeader) return true;
        return b.TargetCardId is { } targetId
            && s.Players[b.DefenderPlayerIndex].Characters.Any(card => card.Id == targetId);
    }

    /// <summary>异步伤害结算：角色 KO 走 KOCardAsync（可被 PreKO 拦截），领袖伤害量返回给调用方处理生命牌</summary>
    public static async Task<int> ResolveDamageAsync(GameState s, IPromptService prompts)
    {
        var b = s.CurrentBattle!;
        var atk = s.Players[b.AttackerPlayerIndex];
        var def = s.Players[b.DefenderPlayerIndex];

        var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
            : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
        if (attacker is null) return 0;
        int attackerPower = s.CurrentPowerOf(b.AttackerPlayerIndex, attacker) + b.AttackerBattleBonus;

        bool attackerWins;
        int leaderDamage = 0;

        if (b.TargetIsLeader)
        {
            int defenderPower = s.CurrentPowerOf(b.DefenderPlayerIndex, def.Leader) + b.DefenderBattleBonus;
            attackerWins = attackerPower + Hex.HexRules.AttackSuccessDeficit(s, b.AttackerPlayerIndex) >= defenderPower;
            if (attackerWins)
                leaderDamage = Validation.ActionValidator.HasKeyword(s, attacker, "双重攻击") ? 2 : 1;
        }
        else
        {
            var target = def.Characters.FirstOrDefault(c => c.Id == b.TargetCardId);
            if (target is null) return 0;
            int defenderPower = s.CurrentPowerOf(b.DefenderPlayerIndex, target) + b.DefenderBattleBonus;
            attackerWins = attackerPower + Hex.HexRules.AttackSuccessDeficit(s, b.AttackerPlayerIndex) >= defenderPower;
            if (attackerWins)
                await KOCardAsync(s, b.DefenderPlayerIndex, target, prompts);
        }
        return leaderDamage;
    }

    /// <summary>战斗结束：清临时修正 + 关键字 + 切回主要阶段</summary>
    public static void EndBattle(GameState s)
    {
        // 记录本回合是否已经与对方角色进行过战斗，供 OP12-020 等战后启动效果判断。
        // 阻挡发生后 TargetIsLeader 会改为 false，因此领袖攻击被角色阻挡也属于与角色战斗。
        var b = s.CurrentBattle;
        if (b is not null && !b.TargetIsLeader)
        {
            var atkP = s.Players[b.AttackerPlayerIndex];
            var atkCard = atkP.Leader.Id == b.AttackerCardId ? atkP.Leader
                : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            if (atkCard is not null)
                atkCard.BattledOpponentCharacterThisTurn = true;
        }
        foreach (var p in s.Players)
        {
            foreach (var c in p.Characters) { c.PowerModThisBattle = 0; }
            p.Leader.PowerModThisBattle = 0;
            foreach (var c in p.Characters) c.GainedKeywords.RemoveAll(k => k.Duration == KeywordDuration.ThisBattle);
            p.Leader.GainedKeywords.RemoveAll(k => k.Duration == KeywordDuration.ThisBattle);
        }
        s.CurrentBattle = null;
        s.Phase = Phase.Main;
    }

    /// <summary>
    /// 异步 KO 流程：
    ///   1. 触发 PreKO（"将要被 KO 的场合改为..." 置换效果可调 state.MarkPreventKO）
    ///   2. 若 PreventKOCardIds 含此卡 → 取消 KO（卡留场上），清除标记
    ///   3. 否则归还附着咚 + 进废弃区 + 触发 OnKO
    /// </summary>
    public static async Task<bool> KOCardAsync(GameState s, int ownerIdx, CardInstance card, IPromptService prompts)
    {
        if (await IsKOReplacedAsync(s, ownerIdx, card, prompts)) return false;

        await CompleteKOAsync(s, ownerIdx, card, prompts);
        return true;
    }

    /// <summary>
    /// Resolves cards that are KO'd by one process. All replacement effects are checked
    /// while every victim is still on the field, so one replacement can cover the whole
    /// matching part of the process (comprehensive rules 8-1-3-4-4).
    /// </summary>
    public static async Task<int> KOCardsSimultaneouslyAsync(
        GameState s, int ownerIdx, IReadOnlyCollection<CardInstance> cards, IPromptService prompts)
    {
        var victims = cards
            .Where(card => s.Players[ownerIdx].Characters.Contains(card))
            .DistinctBy(card => card.Id)
            .ToList();
        if (victims.Count == 0) return 0;

        var previousBatch = s.SimultaneousKOVictimIds;
        var victimIds = victims.Select(card => card.Id).ToHashSet();
        s.SimultaneousKOVictimIds = victimIds;
        foreach (var id in victimIds)
        {
            s.PreventKOCardIds.Remove(id);
            s.PreventLeaveCardIds.Remove(id);
        }

        try
        {
            var replaced = new HashSet<Guid>();
            foreach (var card in victims)
            {
                if (s.IsGameOver) break;
                if (!s.Players[ownerIdx].Characters.Contains(card)) continue;
                if (await IsKOReplacedAsync(s, ownerIdx, card, prompts)) replaced.Add(card.Id);
            }
            replaced.UnionWith(victimIds.Where(id => s.PreventKOCardIds.Contains(id)));

            int count = 0;
            foreach (var card in victims)
            {
                if (s.IsGameOver) break;
                if (replaced.Contains(card.Id) || !s.Players[ownerIdx].Characters.Contains(card)) continue;
                await CompleteKOAsync(s, ownerIdx, card, prompts);
                count++;
            }
            return count;
        }
        finally
        {
            foreach (var id in victimIds)
            {
                s.PreventKOCardIds.Remove(id);
                s.PreventLeaveCardIds.Remove(id);
            }
            s.SimultaneousKOVictimIds = previousBatch;
        }
    }

    internal static async Task<bool> IsKOReplacedAsync(
        GameState s, int ownerIdx, CardInstance card, IPromptService prompts)
    {
        if (s.KOReason == "effect" && EffectRuntime.IsEffectLeaveReplacementCovered(s, ownerIdx, card))
            return true;

        // A replacement may already cover this card as part of the active simultaneous process.
        if (s.SimultaneousKOVictimIds?.Contains(card.Id) == true
            && (s.PreventKOCardIds.Remove(card.Id) || s.PreventLeaveCardIds.Remove(card.Id)))
            return true;

        // 清除非当前批次遗留的单卡标记。批次覆盖已在上方消费，不能让旧标记污染本次 KO。
        s.PreventKOCardIds.Remove(card.Id);
        s.PreventLeaveCardIds.Remove(card.Id);

        // 同一个“将要被 KO”处理点可能同时存在自身置换、守护者与因对方效果离场置换。
        // 它们属于同一方同时待处理的效果，由受影响玩家决定结算顺序；放弃一个可继续选择其它效果。
        var guardSide = s.Players[ownerIdx];
        bool VictimIsOnField()
            => guardSide.Characters.Contains(card)
                || ReferenceEquals(guardSide.StageCard, card)
                || ReferenceEquals(guardSide.ExtraStageCard, card);
        var guardians = new List<CardInstance> { guardSide.Leader };
        guardians.AddRange(guardSide.Characters);
        if (guardSide.StageCard is not null) guardians.Add(guardSide.StageCard);
        if (guardSide.ExtraStageCard is not null) guardians.Add(guardSide.ExtraStageCard);

        var candidates = new List<KOReplacementCandidate>();
        if (EffectRuntime.HasResolvableKOReplacementEffect(s, card, EffectTrigger.PreKO))
            candidates.Add(new KOReplacementCandidate(card, EffectTrigger.PreKO, null));

        var koPayload = new Dictionary<string, object?>
        {
            ["victimId"] = card.Id.ToString(),
            ["victimOwner"] = ownerIdx,
        };
        foreach (var g in guardians)
        {
            if (g.Id == card.Id) continue;
            if (!EffectRuntime.HasResolvableKOReplacementEffect(s, g, EffectTrigger.OnAllyWillBeKOd)) continue;
            candidates.Add(new KOReplacementCandidate(g, EffectTrigger.OnAllyWillBeKOd, koPayload));
        }

        // 效果 KO 还需检查“因对方效果将要离场”类守护；战斗 KO 不进入该分支。
        if (s.KOReason == "effect" && s.KOActingSide >= 0 && s.KOActingSide != ownerIdx)
        {
            var leavePayload = new Dictionary<string, object?>
            {
                ["victimId"] = card.Id.ToString(),
                ["victimOwner"] = ownerIdx,
                ["kind"] = "ko",
            };
            foreach (var g in guardians)
            {
                if (!EffectRuntime.HasResolvableKOReplacementEffect(s, g, EffectTrigger.OnAllyWillLeaveField)) continue;
                // 同一卡实例通常只是为兼容 KO/非 KO 两条引擎路径同时登记两个标签；
                // KO 窗口只能发动一次，优先使用更精确的 OnAllyWillBeKOd 连线。
                if (candidates.Any(candidate => candidate.Source.Id == g.Id)) continue;
                candidates.Add(new KOReplacementCandidate(g, EffectTrigger.OnAllyWillLeaveField, leavePayload));
            }
        }

        while (candidates.Count > 0 && !s.IsGameOver && VictimIsOnField())
        {
            candidates.RemoveAll(candidate =>
                s.SideOf(candidate.Source) != ownerIdx
                || !EffectRuntime.IsTriggeredEffectAvailable(
                    s, ownerIdx, candidate.Source, candidate.Trigger, candidate.Payload));
            if (candidates.Count == 0) break;

            var selected = candidates[0];
            if (candidates.Count > 1)
            {
                var tokens = candidates.Select(candidate => candidate.Token).ToList();
                var chosen = await prompts.ChooseCards(ownerIdx, "EffectOrder",
                    "多个 KO 代替或保护效果同时可用，请选择下一个要结算的效果",
                    tokens, 1, 1,
                    new Dictionary<string, object?>
                    {
                        ["choiceCards"] = candidates.Select(candidate => new
                        {
                            id = candidate.Token,
                            number = candidate.Source.Info.Number,
                            trigger = candidate.Trigger.ToString(),
                        }).ToList(),
                    });
                if (chosen.Count != 1) break;
                var authoritativeSelection = candidates.FirstOrDefault(candidate => candidate.Token == chosen[0]);
                if (authoritativeSelection is null) break;
                selected = authoritativeSelection;
            }

            candidates.Remove(selected);
            // Prompt 等待期间必须再次以权威场上状态和可用性校验，拒绝旧快照或乱序响应支付成本。
            if (s.SideOf(selected.Source) != ownerIdx
                || !VictimIsOnField()
                || !EffectRuntime.IsTriggeredEffectAvailable(
                    s, ownerIdx, selected.Source, selected.Trigger, selected.Payload)) continue;

            await EffectRuntime.Resolve(
                s, ownerIdx, selected.Source, selected.Trigger, prompts, selected.Payload);
            if (!VictimIsOnField()) return true;
            if (s.PreventKOCardIds.Remove(card.Id) || s.PreventLeaveCardIds.Remove(card.Id)) return true;
        }

        string reason = s.KOReason == "effect" ? "effect" : "battle";
        if (s.IsKoGuarded(card, reason)) return true;
        if (reason == "effect" && s.IsLeaveGuarded(card, "effect")) return true;
        if (await TryDiscardHandToPreventKOAsync(s, ownerIdx, card, prompts, reason)) return true;

        return false;
    }

    private sealed record KOReplacementCandidate(
        CardInstance Source,
        EffectTrigger Trigger,
        Dictionary<string, object?>? Payload)
    {
        public string Token => $"{Source.Id}:{Trigger}";
    }

    /// <summary>结算“可以丢弃我方 1 张手牌，使该角色不会被 KO”的限时置换效果。</summary>
    private static async Task<bool> TryDiscardHandToPreventKOAsync(
        GameState s, int ownerIdx, CardInstance card, IPromptService prompts, string reason)
    {
        int side = s.SideOf(card);
        if (side < 0) return false;
        var replacement = s.ContinuousEffects.FirstOrDefault(effect =>
            effect.DiscardHandKoReplacement is not null &&
            (effect.DiscardHandKoReplacement == "any" || effect.DiscardHandKoReplacement == reason) &&
            s.IsContinuousEffectApplicable(effect, side, card));
        if (replacement is null) return false;

        var owner = s.Players[ownerIdx];
        if (owner.Hand.Count == 0) return false;

        bool use = await prompts.ConfirmOptional(ownerIdx,
            $"丢弃我方 1 张手牌，使「{card.Info.Name}」不会被 KO？");
        if (!use) return false;

        var candidates = owner.Hand.ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(candidate => new { id = candidate.Id.ToString(), number = candidate.Info.Number })
                .ToList(),
        };
        List<string> chosen;
        try
        {
            chosen = await prompts.ChooseCards(ownerIdx, "OwnHand",
                "选择丢弃 1 张手牌，使该角色不会被 KO",
                candidates.Select(candidate => candidate.Id.ToString()).ToList(), 1, 1, extra);
        }
        catch (OptionalEffectDeclinedException)
        {
            // 这条置换效果不经过 EffectRuntime；玩家返回确认并改选“不发动”时在此正常退出。
            return false;
        }
        if (chosen.Count == 0) return false;

        var discard = candidates.FirstOrDefault(candidate => candidate.Id.ToString() == chosen[0]);
        if (discard is null || !owner.Hand.Contains(discard)) return false;

        // 此置换发生在战斗流程而非普通 EffectRuntime 上下文中，因此直接派发丢手牌监听，
        // 保持“因效果丢弃手牌”（包括成本）类能力与普通卡牌脚本一致。
        owner.Hand.Remove(discard);
        owner.Trash.Add(discard);
        owner.HandDiscardedByEffectThisTurn = true;
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnHandDiscarded, prompts,
            new Dictionary<string, object?>
            {
                ["owner"] = ownerIdx,
                ["sourceNumber"] = replacement.SourceCardNumber,
                ["actingSide"] = ownerIdx,
                ["isCost"] = true,
                ["cardId"] = discard.Id.ToString(),
                ["cardKind"] = discard.Info.Kind.ToString(),
            });
        return true;
    }

    private static async Task CompleteKOAsync(
        GameState s, int ownerIdx, CardInstance card, IPromptService prompts)
    {
        // 实际 KO：归还附着咚 + 进废弃区
        var p = s.Players[ownerIdx];
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        p.Characters.Remove(card);
        p.Trash.Add(card);
        // 实际 KO 后立即移除来源卡注册的持续效果，避免完整异步 KO 路径留下僵尸光环。
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == card.Id.ToString());

        // OnKO：卡已进入废弃区，但效果在"原场上位置"上发动
        await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.OnKO, prompts);
        string reason = s.KOReason == "effect" ? "effect" : "battle";
        // 任意角色被KO：场上他卡可据此反应（如 EB01-047 拉布 / OP01-061 / OP04-086）。
        // BattleEngine 路径可能不在效果 ambient 内，因此直接 TriggerEvent 立即派发。
        // 携带 attackerId 供"通过此角色战斗KO对方"类效果判定（CurrentBattle 此刻仍未清场）。
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = card.Id.ToString(),
                ["owner"] = ownerIdx,
                ["reason"] = reason,
                ["attackerId"] = reason == "battle" ? s.CurrentBattle?.AttackerCardId.ToString() : null,
                ["actingSide"] = reason == "battle"
                    ? s.CurrentBattle?.AttackerPlayerIndex ?? 1 - ownerIdx
                    : s.KOActingSide,
            });
    }

    /// <summary>同步 KO（保留供内部不会被置换的场景：满员废弃、放回手牌前不需走 KO 等）</summary>
    public static void KOCard(GameState s, int ownerIdx, CardInstance card)
    {
        var p = s.Players[ownerIdx];
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        p.Characters.Remove(card);
        if (ReferenceEquals(p.StageCard, card)) p.StageCard = null; // 舞台卡被KO/送废弃（如 OP14-088 KO对方1费舞台）
        if (ReferenceEquals(p.ExtraStageCard, card)) p.ExtraStageCard = null;
        p.Trash.Add(card);
        // 来源离场即时清理其注册的持续效果：此前仅靠 TurnEngine 结束阶段兜底，
        // 角色被KO后其持续光环会残留到回合末（反馈#245 OP15-092 领袖7000残留）。
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == card.Id.ToString());
    }
}
