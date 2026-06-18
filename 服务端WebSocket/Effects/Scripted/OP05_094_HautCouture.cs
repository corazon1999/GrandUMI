using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-094 高级缝制 七彩★拼接（事件）
/// 【主要】本回合中，对方最多 1 张角色费用 -3。之后，对方最多 1 张费用为 0 的角色，
///   在下个重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 第一段用 AddCostModifier(-3, ThisTurn) 对所选对方角色减费。
///   - "之后"第二段对费用为 0 的对方角色用 PreventActivateNextReset 阻止其下次重置变活跃。
///     费用判定用 CurrentCost()（已包含本回合减费修正，故第一段被减到 0 的角色也可被选中）。
///   - 【触发】部分（抽 2 弃 1）由生命触发节单独处理，本主要脚本不实现。
/// </summary>
public class OP05_094_HautCouture : IScriptedEffect
{
    public string CardNumber => "OP05-094";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 第一段：对方最多 1 张角色费用 -3（本回合中）
        var costCands = opp.Characters.ToList();
        if (costCands.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合费用 -3",
                costCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = costCands.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.AddCostModifier(tgt, -3, KeywordDuration.ThisTurn);
            }
        }

        // 第二段：对方最多 1 张费用为 0 的角色，下个重置阶段不转为活跃
        var zeroCands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 0).ToList();
        if (zeroCands.Count > 0)
        {
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张费用为 0 的角色，下个重置阶段不转为活跃状态",
                zeroCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch2.Count > 0)
            {
                var tgt = zeroCands.First(c => c.Id.ToString() == ch2[0]);
                AtomicOps.PreventActivateNextReset(tgt);
            }
        }
    }
}
