using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-075 福克斯（角色 / 福克斯海盗团）
/// 【启动主要】可以将此角色放置到废弃区：
///   我方场上咚!!的张数不多于对方场上咚!!的张数的场合，抽取 1 张卡牌。
///
/// 实现说明 / 简化点：
///   - 成本为"将此角色放置到废弃区"（可选）。
///   - 条件为两方费用区咚!!总数的动态比较：me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 仅在条件满足时才有抽牌收益；不满足时不提示（支付成本只会白白弃掉自身）。
/// </summary>
public class OP10_075_Foxy : IScriptedEffect
{
    public string CardNumber => "OP10-075";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：我方场上咚总数 ≤ 对方场上咚总数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "福克斯【启动主要】：将此角色放置到废弃区，抽取 1 张卡牌？");
        if (!use) return;

        // 成本：将此角色放到废弃区
        if (me.Characters.Remove(self))
        {
            me.Trash.Add(self);
        }

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
