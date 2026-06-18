using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-060 牛肉轰炸踢（事件）
/// 【主要】我方领袖为多种颜色的场合，选择以下的 1 项：
///   ・将对方最多 1 张费用不高于 4 的角色放回其持有者的手牌。
///   ・我方手牌不多于 6 张的场合，抽取 2 张卡牌。
/// </summary>
public class OP12_060_BeefBomber : IScriptedEffect
{
    public string CardNumber => "OP12-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方领袖为多种颜色
        if (me.Leader.Info.ColorList.Length < 2) return;

        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "牛肉轰炸踢：选择以下的 1 项",
            new List<string>
            {
                "将对方最多 1 张费用不高于 4 的角色放回手牌",
                "我方手牌不多于 6 张时，抽取 2 张卡牌",
            });

        if (choice == 0)
        {
            // ・将对方最多 1 张费用不高于 4 的角色放回其持有者的手牌
            var candidates = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 4).ToList();
            if (candidates.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe4",
                "选择对方最多 1 张费用不高于 4 的角色放回手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;
            var tgt = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
        else
        {
            // ・我方手牌不多于 6 张的场合，抽取 2 张卡牌
            if (me.Hand.Count <= 6)
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        }
    }
}
