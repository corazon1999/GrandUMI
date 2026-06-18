using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-049 八茶（角色）
/// 【咚!!×1】【攻击时】将最多1张费用不高于3的角色放回其持有者的手牌。
///
/// 说明：
///   - 【咚!!×1】为发动条件：此角色身上附着的咚!! ≥1 才发动。
///   - 目标可为双方任一费用≤3角色；按所属方调用 BounceToHand。
///   - 费用按 CurrentCost() 评估。
/// </summary>
public class OP05_049_Hatcha : IScriptedEffect
{
    public string CardNumber => "OP05-049";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】发动条件
        if (me.AttachedDonCount(self.Id) < 1) return;

        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 3))
            cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 3))
            cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多1张费用≤3的角色，放回其持有者手牌",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, picked.owner, picked.card);
        }
    }
}
