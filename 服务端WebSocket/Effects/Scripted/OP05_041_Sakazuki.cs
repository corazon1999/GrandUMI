using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-041 萨卡斯基（领航）
/// 【启动主要】【每回合1次】可以丢弃我方的1张手牌：抽取1张卡牌。
/// 【攻击时】本回合中，对方最多1张角色费用-1。
///
/// 说明 / 简化点：
///   - 两个时机：启动主要（弃1抽1，每回合1次）与攻击时（对方1张角色本回合费用-1）。
///   - 费用-1 用 AddCostModifier(c, -1, ThisTurn) 作用于所选对方角色。
/// </summary>
public class OP05_041_Sakazuki : IScriptedEffect
{
    public string CardNumber => "OP05-041";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = self.Info.Number + "-act" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            if (me.Hand.Count == 0) return;
            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "萨卡斯基【启动主要】：丢弃1张手牌，抽1张？");
            if (!use) return;

            var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (disc.Count < 1) return;
            var card = me.Hand.First(c => c.Id.ToString() == disc[0]);
            AtomicOps.DiscardHand(me, card);
            me.TurnOnceUsed.Add(key);
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // OnAttackDeclare：对方最多1张角色费用-1（本回合）
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多1张对方角色，本回合费用-1",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(tgt, -1, KeywordDuration.ThisTurn);
        }
    }
}
