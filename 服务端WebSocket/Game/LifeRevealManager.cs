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

        // 判断本次攻击者是否带【流放】；并记录攻击者 id 供 OnDamageToLeader 派发
        bool exile = false;
        string? attackerIdForTrigger = null;
        if (s.CurrentBattle is { } b && b.DefenderPlayerIndex == targetPlayerIdx)
        {
            attackerIdForTrigger = b.AttackerCardId.ToString();
            var atk = s.Players[b.AttackerPlayerIndex];
            var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
                : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            if (attacker is not null && ActionValidator.HasKeyword(s, attacker, "流放"))
                exile = true;
        }

        int dealt = 0;
        for (int i = 0; i < damage; i++)
        {
            if (p.LifeArea.Count == 0)
            {
                if (!s.IsGameOver)
                {
                    s.WinnerIndex = 1 - targetPlayerIdx;
                    s.GameOverReason = $"{p.AccountName} 生命耗尽";
                }
                return;
            }

            var top = p.LifeArea[0];
            p.LifeArea.RemoveAt(0);
            dealt++;

            if (exile)
            {
                // 【流放】：直接进废弃区，不触发触发效果，不弹窗
                p.Trash.Add(top);
                continue;
            }

            bool hasTrigger = !string.IsNullOrEmpty(top.Info.Trigger);
            bool forcePrompt = p.AlwaysPromptOnLifeReveal;

            if (hasTrigger || forcePrompt)
            {
                bool useTrigger = await engine.Prompts.AskLifeTrigger(targetPlayerIdx, top, hasTrigger);
                if (useTrigger && hasTrigger)
                {
                    // 发动触发：卡牌进废弃区，效果用 OnLifeRevealTrigger 触发
                    p.Trash.Add(top);
                    await EffectRuntime.Resolve(s, targetPlayerIdx, top,
                        EffectTrigger.OnLifeRevealTrigger, engine.Prompts);
                    // 「此卡牌登场」通用兜底：纯自登场角色(无 DSL/脚本触发逻辑处理它)在此自动从废弃区登场。
                    // 带条件/成本的自登场由各卡 DSL trigger 的 PlaySelf op 处理(那时卡已离开废弃区→不再命中此兜底)。
                    if (!s.IsGameOver && top.Info.Kind == CardKind.Character
                        && p.Trash.Contains(top) && IsPlainPlaySelfTrigger(top.Info.Trigger))
                    {
                        AtomicOps.PlayFromTrashFree(s, targetPlayerIdx, top);
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

            // 生命牌离场 → 派发 watcher（OP05-098 生命变0 / OP08-105 对方生命离场 / OP12-099 等）
            await EffectRuntime.TriggerEvent(s, EffectTrigger.OnLifeLeaveField, engine.Prompts,
                new Dictionary<string, object?> { ["owner"] = targetPlayerIdx, ["toZero"] = p.LifeArea.Count == 0 });
            if (s.IsGameOver) return;
        }

        // 给对方生命区造成了伤害 → 派发 OnDamageToLeader（攻击者卡可据此发动，如 OP03-040/041/043）
        if (dealt > 0 && attackerIdForTrigger is not null && !s.IsGameOver)
            await EffectRuntime.TriggerEvent(s, EffectTrigger.OnDamageToLeader, engine.Prompts,
                new Dictionary<string, object?> { ["attackerId"] = attackerIdForTrigger, ["defenderOwner"] = targetPlayerIdx });
    }

    /// <summary>是否为"纯【触发】此卡牌登场"(无成本「：」、无条件「场合」、无后续「之后」)。
    /// 这类由引擎通用兜底自动从废弃区登场；带条件/成本的自登场卡用各自 DSL trigger 的 PlaySelf op 处理。</summary>
    private static bool IsPlainPlaySelfTrigger(string? trigger)
    {
        if (string.IsNullOrEmpty(trigger) || !trigger.Contains("此卡牌登场")) return false;
        return !trigger.Contains("：") && !trigger.Contains(":") && !trigger.Contains("场合") && !trigger.Contains("之后");
    }

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
        for (int i = 0; i < damage; i++)
        {
            if (p.LifeArea.Count == 0)
            {
                if (!s.IsGameOver)
                {
                    s.WinnerIndex = 1 - targetPlayerIdx;
                    s.GameOverReason = $"{p.AccountName} 生命耗尽";
                }
                return;
            }
            var top = p.LifeArea[0];
            p.LifeArea.RemoveAt(0);
            p.Hand.Add(top);
        }
    }
}
