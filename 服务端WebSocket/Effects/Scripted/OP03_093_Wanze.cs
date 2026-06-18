using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-093 瓦杰（角色 / 地 / CP9）
/// 【登场时】可以丢弃我方的 1 张手牌：我方领袖拥有的特征中包含&lt;普通（C）P&gt;的场合，
///   将对方最多 1 张费用不高于 1 的角色KO。
///
/// 实现说明：
///   - "可以…"=可选；成本为丢弃我方 1 张手牌（与 KO 结果强耦合）。
///   - &lt;普通（C）P&gt; 即 CP 特征：领袖任一特征字符串包含 "CP"（子串匹配）。
///   - 领袖条件不满足时，支付成本也无收益，故仅在领袖含 CP 时询问发动。
/// </summary>
public class OP03_093_Wanze : IScriptedEffect
{
    public string CardNumber => "OP03-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        bool LeaderHasCp = me.Leader.Info.Keywords.Any(k => k.Contains("CP"));
        if (!LeaderHasCp) return;
        if (me.Hand.Count < 1) return;

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 1).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "瓦杰【登场时】：丢弃 1 张手牌，KO 对方最多 1 张费用≤1 的角色？");
        if (!use) return;

        // 成本：丢弃 1 张手牌
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃 1 张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discard.Count < 1) return;
        var dc = me.Hand.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, dc);

        // 收益：KO 对方最多 1 张费用≤1 的角色
        var koExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "KO 对方最多 1 张费用≤1 的角色",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, koExtra);
        if (koPick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == koPick[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
