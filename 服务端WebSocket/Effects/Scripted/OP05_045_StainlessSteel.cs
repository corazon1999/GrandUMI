using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-045 不锈钢（角色）
/// 【启动主要】可以丢弃我方的1张手牌，将此角色转为休息状态：
///   将最多1张费用不高于2的角色（我方或对方均可）放回其持有者的卡组最下方。
///
/// 说明：
///   - 成本：弃1手牌 + 横置自身。用 ConfirmOptional 询问可选发动。
///   - 目标可为双方任一费用≤2角色；按其所属方调用 ReturnFieldToDeckBottom(s, ownerIdxOfTarget, card)。
///   - 费用按 CurrentCost() 评估。
/// </summary>
public class OP05_045_StainlessSteel : IScriptedEffect
{
    public string CardNumber => "OP05-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Hand.Count == 0) return;

        // 候选：双方费用≤2角色，记录所属方
        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2))
            cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2))
            cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "不锈钢【启动主要】：丢弃1张手牌并横置此角色，将1张费用≤2的角色放回其持有者卡组底？");
        if (!use) return;

        // 成本：弃1手牌
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃1张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (disc.Count < 1) return;
        var dcard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 效果：选最多1张费用≤2角色放回其持有者卡组底
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多1张费用≤2的角色，放回其持有者卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.owner, picked.card);
        }
    }
}
