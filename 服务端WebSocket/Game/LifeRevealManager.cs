using GrandUMI.Effects;

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
    /// </summary>
    public static async Task DealDamageToLeader(GameEngine engine, int targetPlayerIdx, int damage)
    {
        var s = engine.State;
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

            bool hasTrigger = !string.IsNullOrEmpty(top.Info.Trigger);
            bool forcePrompt = p.AlwaysPromptOnLifeReveal;

            if (hasTrigger || forcePrompt)
            {
                bool useTrigger = await engine.Prompts.AskLifeTrigger(targetPlayerIdx, top, hasTrigger);
                if (useTrigger && hasTrigger)
                {
                    // 发动触发：卡牌进废弃区（除非有【流放】），效果用 OnLifeRevealTrigger 触发
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
