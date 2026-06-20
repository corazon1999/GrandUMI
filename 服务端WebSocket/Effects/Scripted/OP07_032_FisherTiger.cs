using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-032 费雪·泰格（角色）
/// 此角色可以在登场的回合中攻击角色。（持续特殊许可，无引擎通道，本脚本不处理）
/// 【登场时】我方领袖拥有《鱼人族》或《人鱼族》特征的场合，可以将对方最多 1 张
///   费用不高于 6 的角色转为休息状态。
///
/// 说明 / 简化点：
///   - "可在登场回合攻击角色"是持续型特殊许可，引擎无对应通道，仅实现【登场时】主动效果部分。
///   - 领袖条件为"鱼人族 OR 人鱼族"，C# 直接用 || 表达。
///   - "转为休息状态"用 AtomicOps.RestCard；费用判定用 CurrentCost()。
/// </summary>
public class OP07_032_FisherTiger : IScriptedEffect
{
    public string CardNumber => "OP07-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 领袖需拥有《鱼人族》或《人鱼族》特征
        if (!me.Leader.Info.HasKeyword("鱼人族") && !me.Leader.Info.HasKeyword("人鱼族")) return;

        var cands = opp.Characters.Where(c => !c.IsTapped && ctx.State.CurrentCostOf(c) <= 6).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "费雪·泰格【登场时】：将对方最多 1 张费用≤6 的角色转为休息状态？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤6 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
