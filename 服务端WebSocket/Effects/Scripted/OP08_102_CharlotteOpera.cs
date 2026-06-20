using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-102 夏洛特·欧培拉（角色）
/// 【登场时】可以丢弃我方的 1 张手牌：将对方最多 1 张费用不高于我方生命卡牌张数的角色 KO。
///
/// 实现说明 / 简化点：
///   - "可以丢弃我方 1 张手牌"为发动成本：先 ConfirmOptional，再选 1 张手牌丢弃，成本支付后才 KO。
///   - KO 费用上限"我方生命卡牌张数"为动态值，直接用 me.LifeCount；候选费用 c.CurrentCost()。
/// </summary>
public class OP08_102_CharlotteOpera : IScriptedEffect
{
    public string CardNumber => "OP08-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "欧培拉【登场时】：丢弃我方 1 张手牌，将对方 1 张费用≤我方生命张数的角色 KO？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardPick.Count == 0) return; // 未支付成本
        var toDiscard = me.Hand.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 效果：KO 对方最多 1 张费用 ≤ 我方生命卡牌张数的角色
        int threshold = me.LifeCount;
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= threshold).ToList();
        if (cands.Count == 0) return;
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold} 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (koPick.Count > 0)
        {
            var koTgt = cands.First(c => c.Id.ToString() == koPick[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, koTgt);
        }
    }
}
