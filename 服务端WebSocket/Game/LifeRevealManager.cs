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

        // 判断本次攻击者是否带【流放】
        bool exile = false;
        if (s.CurrentBattle is { } b && b.DefenderPlayerIndex == targetPlayerIdx)
        {
            var atk = s.Players[b.AttackerPlayerIndex];
            var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
                : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
            if (attacker is not null && ActionValidator.HasKeyword(attacker, "流放"))
                exile = true;
        }

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
                }
                else
                {
                    p.Hand.Add(top);
                }
            }
            else
            {
                p.Hand.Add(top);
            }
        }
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
