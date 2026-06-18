using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-026 贝莱特王子（角色 / 水 / 因佩尔地狱）
/// 【咚!!×1】【攻击时】我方手牌不多于 1 张的场合，
///   将最多 1 张费用不高于 3 的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 【咚!!×1】为发动条件：本卡被赋予中的咚!! ≥1（AttachedDonCount）。
///   - 【攻击时】触发；附带条件"我方手牌不多于 1 张"。
///   - 目标为"任一持有者"的费用≤3 角色（含敌我双方），用 BounceToHand 放回其持有者手牌。
///   - 候选为非公开身份混合？此处均为场上公开角色，prompt 可直接给 id 列表。
/// </summary>
public class EB01_026_PrinceBellett : IScriptedEffect
{
    public string CardNumber => "EB01-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】发动条件：本卡被赋予中的咚!! ≥1
        if (me.AttachedDonCount(self.Id) < 1) return;
        // 我方手牌不多于 1 张
        if (me.Hand.Count > 1) return;

        // 候选：双方费用≤3 的角色
        var mine = me.Characters.Where(c => c.Info.Cost <= 3).Select(c => (c, ctx.OwnerIndex)).ToList();
        var theirs = opp.Characters.Where(c => c.Info.Cost <= 3).Select(c => (c, 1 - ctx.OwnerIndex)).ToList();
        var cands = new List<(CardInstance card, int owner)>();
        cands.AddRange(mine);
        cands.AddRange(theirs);
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用不高于 3 的角色放回其持有者的手牌（含敌我）",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(ctx.State, picked.owner, picked.card);
    }
}
