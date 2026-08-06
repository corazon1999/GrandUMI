using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-060 战国（领航）
/// 启动主要：将 8 张活跃咚放回咚卡组 → 从手牌选最多 3 张卡名不同的《大将》角色登场
/// </summary>
public class OP16_060_Sengoku : IScriptedEffect
{
    public string CardNumber => "OP16-060";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.ActiveDonCount < 8) return;

        // 支付：把 8 张咚放回咚卡组
        AtomicOps.ReturnDonToDeck(me, 8);

        // 从手牌选最多 3 张卡名不同的《大将》角色登场
        // 同名卡的任意一张在本效果中等价，候选中仅保留一张以在服务端直接保证“卡名不同”。
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.HasKeyword("大将"))
            .GroupBy(c => c.Info.Name)
            .Select(group => group.First())
            .ToList();
        if (candidates.Count == 0) return;

        // 一次性选定，顺序即后续各卡【登场时】效果的结算顺序。
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandShogun",
            "选择最多 3 张卡名不同的《大将》角色登场（按选择顺序结算登场时效果）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(3, candidates.Count));

        foreach (var cardId in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == cardId);
            if (card is not null && me.Hand.Contains(card))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        }
    }
}
