using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-024 阿拉丁（角色 / 风）
/// 当此角色因对方的效果而被KO时，可以丢弃我方的1张手牌，并将我方的1张咚!!转为休息状态。
///   那样做的场合，将我方手牌中最多1张费用不高于6且拥有《鱼人族》或《人鱼族》特征的角色卡牌登场。
/// 实现：OnKO，仅当 KOReason=="effect" 且发起方为对方时；可选成本(弃1手牌+休息1咚)，付出后从手牌登场1张≤6费鱼人/人鱼角色。
/// </summary>
public class OP11_024_Aladine : IScriptedEffect
{
    public string CardNumber => "OP11-024";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide == ctx.OwnerIndex) return; // 须因对方效果被KO
        if (me.Hand.Count == 0) return;
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;                                    // 成本需1张活跃咚

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿拉丁：丢弃1张手牌并休息1张咚，登场1张≤6费《鱼人族/人鱼族》角色？");
        if (!use) return;

        // 成本：弃1手牌
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (ch.Count == 0) return;
        var disc = me.Hand.FirstOrDefault(c => c.Id.ToString() == ch[0]);
        if (disc is null) return;
        AtomicOps.DiscardHand(me, disc);
        // 成本：休息1张活跃咚
        don.State = DonState.Rest;

        // 收益：从手牌登场最多1张≤6费且含《鱼人族》或《人鱼族》的角色
        var cands = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 6
            && (c.Info.HasKeyword("鱼人族") || c.Info.HasKeyword("人鱼族"))).ToList();
        if (cands.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多1张≤6费《鱼人族/人鱼族》角色", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count == 0) return;
        var card = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
        if (card is not null) AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }
}
