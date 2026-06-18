using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-104 山智（角色 / 水）
/// 【阻挡者】（关键词，由引擎处理）
/// 【触发】可以丢弃我方的1张手牌：此卡牌登场。
///
/// 实现：OnLifeRevealTrigger，可选支付成本（弃1张手牌）后将此卡从废弃区登场。
/// </summary>
public class OP04_104_Sanji : IScriptedEffect
{
    public string CardNumber => "OP04-104";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return;                                      // 无手牌可弃，无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "山智：丢弃我方1张手牌，使此卡登场？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is null) return;
        AtomicOps.DiscardHand(me, card);

        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
