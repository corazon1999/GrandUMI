using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-052 马尔高（角色 / 水 3 费 5000，白胡子海盗团）
/// 【对方的回合中】可以丢弃我方的 1 张手牌：当此角色因对方的效果而被 KO 时，
///   从废弃区中将此角色卡牌以休息状态登场。
///
/// 实现说明 / 简化点：
///   - 用 OnKO 钩子（此卡被 KO、已进入废弃区时触发，ctx.Source 即本卡）。
///   - "因对方的效果而被 KO" 简化为 "在对方的回合中被 KO"（引擎未区分 KO 来源是战斗还是效果）。
///   - 成本 "丢弃我方 1 张手牌" 为可选（"可以…"），无手牌则无法支付。
///   - 登场为休息状态：PlayFromTrashFree(restState: true)。
/// </summary>
public class OP09_052_Marco : IScriptedEffect
{
    public string CardNumber => "OP09-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅在对方的回合中可发动
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 此角色须确实在废弃区（被 KO 后由引擎放入）
        if (!me.Trash.Contains(ctx.Source)) return;

        // 需要可丢弃的手牌作为成本
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "马尔高被 KO：丢弃我方 1 张手牌，从废弃区将此角色以休息状态登场？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardPick.Count < 1) return;
        var discard = me.Hand.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, discard);

        // 从废弃区将此角色以休息状态登场
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: true);
    }
}
