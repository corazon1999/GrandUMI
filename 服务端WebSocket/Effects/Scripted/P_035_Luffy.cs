using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-035 蒙奇·D·路飞（角色 / 地 / 草帽一伙，cost6 power6000）
/// 【咚!!×1】【攻击时】可以丢弃我方的 1 张手牌：将对方最多 1 张费用为 0 的角色 KO。
///
/// 实现说明：
///   - 【咚!!×1】：本卡被赋予中的咚!! ≥1 才能发动。
///   - 成本"丢弃 1 张手牌"为可选：先 ConfirmOptional，手牌为空则无法支付成本。
///   - 费用判定走 CurrentCostOf（含持续费用修正），KO 对方角色用 1 - OwnerIndex。
/// </summary>
public class P_035_Luffy : IScriptedEffect
{
    public string CardNumber => "P-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 需有手牌支付成本
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【攻击时】：丢弃 1 张手牌，将对方最多 1 张费用为 0 的角色 KO？");
        if (!use) return;

        // 成本：丢弃 1 张手牌
        var discardChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardChoice.Count < 1) return; // 未支付成本
        var toDiscard = me.Hand.First(c => c.Id.ToString() == discardChoice[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 收益：KO 对方最多 1 张费用为 0 的角色
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) == 0)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用为 0 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
