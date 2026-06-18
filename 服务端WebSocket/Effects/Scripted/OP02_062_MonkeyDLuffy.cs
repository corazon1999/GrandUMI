using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-062 蒙奇·D·路飞（角色 / 水 cost6）
/// 【登场时】/【攻击时】可以丢弃我方的 2 张手牌：将最多 1 张费用不高于 4 的角色放回其持有者的手牌中。
///   之后，本回合中，此角色获得【双重攻击】效果。
///
/// 实现说明：
///   - 登场时 / 攻击时同一效果，成本为可选丢弃我方 2 张手牌（手牌不足 2 则无法发动）。
///   - 「可以」=可选，先 ConfirmOptional；确认后选 2 张手牌丢弃支付成本。
///   - 收益：将最多 1 张费用≤4 的角色(双方场上任意)放回其持有者手牌；之后本回合此角色获得【双重攻击】。
/// </summary>
public class OP02_062_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "OP02-062";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Hand.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞：丢弃我方 2 张手牌，将最多 1 张费用≤4 的角色放回手牌，并令此角色本回合获得【双重攻击】？");
        if (!use) return;

        // 成本：丢弃我方 2 张手牌
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方的 2 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (disc.Count < 2) return; // 未完成成本支付
        foreach (var id in disc)
        {
            var card = me.Hand.First(c => c.Id.ToString() == id);
            AtomicOps.DiscardHand(me, card);
        }

        // 收益：将最多 1 张费用≤4 的角色放回其持有者手牌（双方场上）
        var cands = new List<(CardInstance card, int ownerIdx)>();
        foreach (var c in me.Characters.Where(c => c.Info.Cost <= 4)) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => c.Info.Cost <= 4)) cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
                "将最多 1 张费用≤4 的角色放回其持有者手牌",
                cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
                AtomicOps.BounceToHand(ctx.State, picked.ownerIdx, picked.card);
            }
        }

        // 之后：本回合中，此角色获得【双重攻击】
        AtomicOps.GiveKeyword(self, "双重攻击", KeywordDuration.ThisTurn);
    }
}
