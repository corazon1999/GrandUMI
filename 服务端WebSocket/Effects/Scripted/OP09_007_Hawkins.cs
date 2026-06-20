using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-007 火特（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】本回合中，我方最多 1 张力量不高于 4000 的领袖力量 +1000。
///
/// 说明 / 简化点：
/// - 领袖仅 1 张；当其当前力量 ≤4000 时提供该收益（最多 1 张）。
/// - 力量评估用 ctx.State.CurrentPowerOf 取含修正后的当前力量进行阈值判断。
/// </summary>
public class OP09_007_Hawkins : IScriptedEffect
{
    public string CardNumber => "OP09-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var leader = me.Leader;

        // 领袖当前力量 ≤4000 才符合目标条件
        if (ctx.State.CurrentPowerOf(ctx.OwnerIndex, leader) > 4000) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
            "选择我方领袖（力量≤4000）本回合 +1000（最多 1 张）",
            new List<string> { leader.Id.ToString() }, 0, 1);
        if (chosen.Count > 0)
        {
            AtomicOps.AddPowerThisTurn(leader, 1000);
        }
    }
}
