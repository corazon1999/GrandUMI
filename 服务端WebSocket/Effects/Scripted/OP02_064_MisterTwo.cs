using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-064 Mr.2·盆·岁末(本萨姆)（角色 / 水 cost5）
/// 【咚!!×1】【攻击时】可以丢弃我方的 1 张手牌：将最多 1 张费用不高于 2 的角色放回其持有者的卡组最下方。
///   之后，本次战斗结束时，将此角色放回持有者的卡组最下方。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】发动门槛：本卡被赋予的咚 ≥1 时才发动（AttachedDonCount）。
///   - 成本为可选丢弃我方 1 张手牌；「可以」=可选，先 ConfirmOptional，确认后弃 1 张支付。
///   - 收益：将最多 1 张费用≤2 的角色(双方场上)放回其持有者卡组最下方。
///   - 「之后，本次战斗结束时将此角色放回卡组最下方」属延迟到战斗结束的连锁，引擎无"本次战斗结束时"钩子，
///     此下行后果(放回自身)未实现，仅实现可获取收益。
/// </summary>
public class OP02_064_MisterTwo : IScriptedEffect
{
    public string CardNumber => "OP02-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】门槛
        if (me.AttachedDonCount(self.Id) < 1) return;
        if (me.Hand.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.2【攻击时】：丢弃我方 1 张手牌，将最多 1 张费用≤2 的角色放回其持有者卡组最下方？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方的 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (disc.Count < 1) return;
        var dcard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 收益：将最多 1 张费用≤2 的角色放回其持有者卡组最下方（双方场上）
        var cands = new List<(CardInstance card, int ownerIdx)>();
        foreach (var c in me.Characters.Where(c => c.Info.Cost <= 2)) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => c.Info.Cost <= 2)) cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤2 的角色放回其持有者卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.ownerIdx, picked.card);
        }
    }
}
