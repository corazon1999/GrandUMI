using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-105 阿悟（角色）
/// 【触发】可以丢弃我方的 1 张手牌：此卡牌登场。
///
/// 实现说明 / 简化点：
///   - 该效果只有【触发】（生命牌触发），故 HandlesTrigger 仅处理 OnLifeRevealTrigger。
///   - 生命触发发动时，引擎已将本卡放入废弃区（见 LifeRevealManager）。因此"此卡牌登场"
///     用 PlayFromTrashFree 从废弃区登场实现。
///   - "可以丢弃我方 1 张手牌"为登场成本：用 ConfirmOptional 询问；选择不丢则不登场，本卡留在废弃区。
///   - 手牌为空时无法支付成本 → 不登场。
/// </summary>
public class OP05_105_Aisa : IScriptedEffect
{
    public string CardNumber => "OP05-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：丢弃我方 1 张手牌（手牌为空无法支付）
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿悟【触发】：丢弃我方 1 张手牌，使此卡牌登场？");
        if (!use) return;

        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方 1 张手牌（成本）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (discardPick.Count < 1) return; // 未支付成本

        var toDiscard = me.Hand.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 此卡牌登场（发动触发时本卡已在废弃区）
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
    }
}
