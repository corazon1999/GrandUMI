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
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnOppAttackDeclare, prompts,
            new Dictionary<string, object?> { ["AttackerIdx"] = s.CurrentBattle!.AttackerPlayerIndex });
        if (s.IsGameOver) return;
        s.Phase = Phase.BattleBlock;
    }

    /// <summary>防守方宣言阻挡者（同步） + 触发【阻挡时】（异步）</summary>
    public static void DeclareBlocker(GameState s, Guid blockerId)
    {
        var b = s.CurrentBattle!;
        b.BlockerDeclared = true;
        b.ReplacedByBlockerCardId = blockerId;
        var defender = s.Players[b.DefenderPlayerIndex];
        var blocker = defender.Characters.First(c => c.Id == blockerId);
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
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnBlockDeclare, prompts);
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

    /// <summary>异步伤害结算：角色 KO 走 KOCardAsync（可被 PreKO 拦截），领袖伤害量返回给调用方处理生命牌</summary>
    public static async Task<int> ResolveDamageAsync(GameState s, IPromptService prompts)
    {
        var b = s.CurrentBattle!;
        var atk = s.Players[b.AttackerPlayerIndex];
        var def = s.Players[b.DefenderPlayerIndex];

        var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
            : atk.Characters.First(c => c.Id == b.AttackerCardId);
        int attackerPower = s.CurrentPowerOf(b.AttackerPlayerIndex, attacker) + b.AttackerBattleBonus;

        bool attackerWins;
        int leaderDamage = 0;

        if (b.TargetIsLeader)
        {
            int defenderPower = s.CurrentPowerOf(b.DefenderPlayerIndex, def.Leader) + b.DefenderBattleBonus;
            attackerWins = attackerPower >= defenderPower;
            if (attackerWins)
                leaderDamage = Validation.ActionValidator.HasKeyword(s, attacker, "双重攻击") ? 2 : 1;
        }
        else
        {
            var target = def.Characters.First(c => c.Id == b.TargetCardId);
            int defenderPower = s.CurrentPowerOf(b.DefenderPlayerIndex, target) + b.DefenderBattleBonus;
            attackerWins = attackerPower >= defenderPower;
            if (attackerWins)
                await KOCardAsync(s, b.DefenderPlayerIndex, target, prompts);
        }
        return leaderDamage;
    }

    /// <summary>战斗结束：清临时修正 + 关键字 + 切回主要阶段</summary>
    public static void EndBattle(GameState s)
    {
        // OP12-020：与对方角色战斗后，带此标记的攻击者转为活跃
        var b = s.CurrentBattle;
        if (b is not null && !b.TargetIsLeader)
        {
            var atkP = s.Players[b.AttackerPlayerIndex];
            var atkCard = atkP.Leader.Id == b.AttackerCardId ? atkP.Leader
                : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            if (atkCard is not null && atkCard.ReactivateAfterBattleThisTurn) atkCard.IsTapped = false;
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
        foreach (var id in victimIds) s.PreventKOCardIds.Remove(id);

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
            foreach (var id in victimIds) s.PreventKOCardIds.Remove(id);
            s.SimultaneousKOVictimIds = previousBatch;
        }
    }

    private static async Task<bool> IsKOReplacedAsync(
        GameState s, int ownerIdx, CardInstance card, IPromptService prompts)
    {
        // A replacement may already cover this card as part of the active simultaneous process.
        if (s.SimultaneousKOVictimIds?.Contains(card.Id) == true && s.PreventKOCardIds.Remove(card.Id))
            return true;

        // PreKO 触发：仅本卡的效果有机会拦截（缩小范围避免误触发其他卡）
        s.PreventKOCardIds.Remove(card.Id);
        if (EffectRuntime.HasEffectForTrigger(card, EffectTrigger.PreKO))
        {
            await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.PreKO, prompts);
        }
        if (s.PreventKOCardIds.Contains(card.Id))
        {
            s.PreventKOCardIds.Remove(card.Id);
            return true; // KO 被取消
        }
        // 守护者：他卡"将要被KO时改为…代替被KO/使其不被KO"置换效果
        var guardSide = s.Players[ownerIdx];
        var guardians = new List<CardInstance> { guardSide.Leader };
        guardians.AddRange(guardSide.Characters);
        if (guardSide.StageCard is not null) guardians.Add(guardSide.StageCard);
        foreach (var g in guardians.ToList())
        {
            if (g.Id == card.Id) continue;
            if (!EffectRuntime.HasEffectForTrigger(g, EffectTrigger.OnAllyWillBeKOd)) continue;
            await EffectRuntime.Resolve(s, ownerIdx, g, EffectTrigger.OnAllyWillBeKOd, prompts,
                new Dictionary<string, object?> { ["victimId"] = card.Id.ToString(), ["victimOwner"] = ownerIdx });
            if (s.PreventKOCardIds.Contains(card.Id)) { s.PreventKOCardIds.Remove(card.Id); return true; }
        }
        // 持续"战斗中不会被KO"保护
        if (s.IsKoGuarded(card, "battle")) return true;

        return false;
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

        // OnKO：卡已进入废弃区，但效果在"原场上位置"上发动
        await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.OnKO, prompts);
        // 任意角色被KO（战斗）：场上他卡可据此反应（如 EB01-047 拉布 / OP01-061 / OP04-086）。
        // 战斗 KO 不在效果上下文内（无 ambient），NotifyWatcher 会被丢弃，故直接 TriggerEvent 立即派发。
        // 携带 attackerId 供"通过此角色战斗KO对方"类效果判定（CurrentBattle 此刻仍未清场）。
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnAnyCharKOd, prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = card.Id.ToString(),
                ["owner"] = ownerIdx,
                ["reason"] = "battle",
                ["attackerId"] = s.CurrentBattle?.AttackerCardId.ToString(),
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
        p.Trash.Add(card);
        // 来源离场即时清理其注册的持续效果：此前仅靠 TurnEngine 结束阶段兜底，
        // 角色被KO后其持续光环会残留到回合末（反馈#245 OP15-092 领袖7000残留）。
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == card.Id.ToString());
    }
}
