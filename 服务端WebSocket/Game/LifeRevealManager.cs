using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game.Validation;

namespace GrandUMI.Game;

/// <summary>
/// 生命牌伤害与触发管理（含反信息泄露弹窗）
///
/// 反信息泄露设计：
///   - 默认：只有真带【触发】的生命牌才弹"发动/加入手牌"窗口
///   - 玩家个人设置 alwaysPromptOnLifeReveal=true 时：所有生命牌都弹窗，
///     对手只能看到"对方正在选择"，无法通过弹窗时机/无弹窗推断生命牌内容
/// </summary>
public static class LifeRevealManager
{
    /// <summary>
    /// 领袖受到 damage 点伤害（异步，因为可能触发 prompt 等待玩家响应）
    ///
    /// 【流放】处理：若攻击者带【流放】关键字，生命牌直接进废弃区，不发动触发；
    /// 反信息泄露弹窗也跳过（因为对手知道你攻击者有【流放】，无法泄露）。
    /// </summary>
    public static async Task DealDamageToLeader(GameEngine engine, int targetPlayerIdx, int damage)
    {
        var s = engine.State;
        var p = s.Players[targetPlayerIdx];

        if (damage <= 0) return;

        // 胜利条件只看本次伤害开始时是否已经没有生命。
        // 双重攻击等多点伤害不会让超出剩余生命的部分穿透并直接获胜。
        if (p.LifeArea.Count == 0)
        {
            if (!s.IsGameOver)
            {
                s.WinnerIndex = 1 - targetPlayerIdx;
                s.GameOverReason = $"{p.VisibleName} 生命耗尽";
            }
            return;
        }

        // 判断本次攻击者是否带【流放】；并记录攻击者 id 供 OnDamageToLeader 派发
        bool exile = false;
        string? attackerIdForTrigger = null;
        CardInstance? damageAttacker = null;
        int attackerSide = 1 - targetPlayerIdx;
        if (s.CurrentBattle is { } b && b.DefenderPlayerIndex == targetPlayerIdx)
        {
            attackerIdForTrigger = b.AttackerCardId.ToString();
            attackerSide = b.AttackerPlayerIndex;
            var atk = s.Players[b.AttackerPlayerIndex];
            var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
                : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            damageAttacker = attacker;
            if (attacker is not null && ActionValidator.HasKeyword(s, attacker, "流放"))
                exile = true;
        }

        int dealt = 0;
        for (int i = 0; i < damage; i++)
        {
            if (p.LifeArea.Count == 0)
                break;

            var top = p.LifeArea[0];
            p.LifeArea.RemoveAt(0);
            s.LifeLeftThisTurn.Add(targetPlayerIdx);
            dealt++;
            // “生命变为 0 张”在生命牌离开的一刻成立；不能等该生命牌的【触发】
            // 补回生命后才判断，否则 OP05-098 会漏掉与 OP06-115 等同时满足的时点。
            bool lifeBecameZero = p.LifeArea.Count == 0;
            bool lifeReachedOne = p.LifeArea.Count == 1;

            if (exile)
            {
                // 【流放】：直接进废弃区，不触发触发效果，不弹窗
                p.Trash.Add(top);
            }
            else
            {
                bool hasTrigger = !string.IsNullOrEmpty(top.Info.Trigger);
                bool forcePrompt = p.AlwaysPromptOnLifeReveal;

                if (hasTrigger || forcePrompt)
                {
                    bool useTrigger = await engine.Prompts.AskLifeTrigger(targetPlayerIdx, top, hasTrigger);
                    if (useTrigger && hasTrigger)
                    {
                        // 发动触发：卡牌进废弃区。
                        // “发动此卡牌的【主要】/【反击】/【登场时】/【KO时】效果”是元触发，
                        // 直接复用对应时机，避免每张卡重复维护一份 trigger 定义。
                        p.Trash.Add(top);
                        var revealTrigger = ResolveLifeTrigger(top.Info.Trigger);
                        await EffectRuntime.Resolve(s, targetPlayerIdx, top,
                            revealTrigger, engine.Prompts, lifeTriggerOrigin: true);
                        // 「此卡牌登场」通用兜底：纯自登场角色或舞台（无 DSL/脚本触发逻辑处理它）
                        // 在此自动从废弃区登场。带条件/成本的自登场由各卡 DSL trigger 的 PlaySelf op
                        // 处理；该卡届时已离开废弃区，因此不会再次命中此兜底。
                        if (!s.IsGameOver && top.Info.Kind is CardKind.Character or CardKind.Stage
                            && p.Trash.Contains(top) && IsPlainPlaySelfTrigger(top.Info.Trigger))
                        {
                            await AtomicOps.PlayFromTrashFree(
                                s, targetPlayerIdx, top, lifeTriggerOrigin: true);
                            // PlayFromTrashFree 只把【登场时】加入延迟队列；这条兜底位于 Resolve 之外，
                            // 必须显式排空一次。角色与舞台均由同一队列确保【登场时】恰好结算一次。
                            if (!s.IsGameOver)
                                await EffectRuntime.DrainPendingEnterFields(s, engine.Prompts);
                        }
                        // 元触发：当(本方)发动【触发】时（OP05-109 帕加亚）
                        if (!s.IsGameOver)
                            await EffectRuntime.TriggerEvent(s, EffectTrigger.OnTriggerActivated, engine.Prompts,
                                new Dictionary<string, object?> { ["owner"] = targetPlayerIdx });
                    }
                    else
                    {
                        AddRevealedLifeToHandOrDeck(p, top);
                    }
                }
                else
                {
                    AddRevealedLifeToHandOrDeck(p, top);
                }
            }

            // 生命牌离场 → 派发 watcher（OP05-098 生命变0 / OP08-105 对方生命离场 / OP12-099 等）
            await EffectRuntime.TriggerEvent(s, EffectTrigger.OnLifeLeaveField, engine.Prompts,
                new Dictionary<string, object?> { ["owner"] = targetPlayerIdx, ["toZero"] = lifeBecameZero });
            if (s.IsGameOver) return;
            if (lifeReachedOne)
            {
                await Hex.HexRules.OnEnemyLifeReachedOneAsync(engine, attackerSide);
                if (s.IsGameOver) return;
            }
            // 双重攻击在本次伤害中曾把生命降到 0 后，即使 OP05-098 等效果补回生命，
            // 其余伤害也不会继续揭开刚补回的生命牌。
            if (lifeBecameZero) break;
        }

        // 给对方生命区造成了伤害 → 派发 OnDamageToLeader（攻击者卡可据此发动，如 OP03-040/041/043）
        if (dealt > 0 && !s.IsGameOver)
        {
            await Hex.HexRules.OnLeaderDamagedAsync(engine, targetPlayerIdx, dealt, damageAttacker);
            if (attackerIdForTrigger is not null && !s.IsGameOver)
                await EffectRuntime.TriggerEvent(s, EffectTrigger.OnDamageToLeader, engine.Prompts,
                    new Dictionary<string, object?> { ["attackerId"] = attackerIdForTrigger, ["defenderOwner"] = targetPlayerIdx });
        }
    }

    /// <summary>是否为"纯【触发】此卡牌登场"(无成本「：」、无条件「场合」、无后续「之后」)。
    /// 这类角色或舞台由引擎通用兜底自动从废弃区登场；带条件/成本的自登场卡用各自 DSL trigger 的 PlaySelf op 处理。</summary>
    private static bool IsPlainPlaySelfTrigger(string? trigger)
    {
        if (string.IsNullOrEmpty(trigger) || !trigger.Contains("此卡牌登场")) return false;
        return !trigger.Contains("：") && !trigger.Contains(":") && !trigger.Contains("场合") && !trigger.Contains("之后");
    }

    private static EffectTrigger ResolveLifeTrigger(string? trigger)
    {
        if (InvokesOwnEffect(trigger, "【KO时】") || InvokesOwnEffect(trigger, "【K.O.时】"))
            return EffectTrigger.OnKO;
        if (InvokesOwnEffect(trigger, "【主要】"))
            return EffectTrigger.EventMain;
        if (InvokesOwnEffect(trigger, "【反击】"))
            return EffectTrigger.EventCounter;
        if (InvokesOwnEffect(trigger, "【登场时】"))
            return EffectTrigger.OnEnterField;
        return EffectTrigger.OnLifeRevealTrigger;
    }

    private static bool InvokesOwnEffect(string? trigger, string effectTiming)
        => !string.IsNullOrEmpty(trigger)
           && trigger.Contains("发动此卡牌的")
           && trigger.Contains(effectTiming)
           && trigger.Contains("效果");

    /// <summary>将受到伤害而揭开的生命牌加入手牌；ST13-003 规则替换：领袖为 ST13-003 时，正面朝上的生命牌改为放回卡组最下方。</summary>
    private static void AddRevealedLifeToHandOrDeck(PlayerState p, CardInstance top)
    {
        if (top.IsLifeFaceUp && p.Leader.Info.Number == "ST13-003")
        {
            top.IsLifeFaceUp = false;
            p.Deck.Add(top); // 卡组最下方
            return;
        }
        p.Hand.Add(top);
    }
}

/// <summary>同步版本：仅在不需要 Prompt 的内部场景使用</summary>
public static class LifeRevealManagerSync
{
    public static void DealDamageToLeaderNoPrompt(GameState s, int targetPlayerIdx, int damage)
    {
        var p = s.Players[targetPlayerIdx];

        if (damage <= 0) return;

        if (p.LifeArea.Count == 0)
        {
            if (!s.IsGameOver)
            {
                s.WinnerIndex = 1 - targetPlayerIdx;
                s.GameOverReason = $"{p.VisibleName} 生命耗尽";
            }
            return;
        }

        for (int i = 0; i < damage; i++)
        {
            if (p.LifeArea.Count == 0)
                break;
            var top = p.LifeArea[0];
            p.LifeArea.RemoveAt(0);
            s.LifeLeftThisTurn.Add(targetPlayerIdx);
            p.Hand.Add(top);
        }
    }
}
