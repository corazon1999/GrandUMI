using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-088 希流（角色）
/// 【咚!!×1】【攻击时】可以丢弃我方的 2 张手牌：抽取 2 张卡牌。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】为发动条件：此角色被赋予中的咚!! ≥1。
///   - 可选成本"丢弃我方 2 张手牌"用 ConfirmOptional 询问 + ChooseCards 选弃 2 张；
///     成本与抽 2 强耦合，须支付满 2 张才抽。
/// </summary>
public class OP09_088_Shiryu : IScriptedEffect
{
    public string CardNumber => "OP09-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 发动条件【咚!!×1】：此角色被赋予中的咚!! ≥1
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 需手牌 ≥2 才能支付成本
        if (me.Hand.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "希流【攻击时】：丢弃我方 2 张手牌，抽取 2 张卡牌？");
        if (!use) return;

        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (discard.Count < 2) return; // 未完成弃 2 → 成本未支付

        foreach (var id in discard)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
