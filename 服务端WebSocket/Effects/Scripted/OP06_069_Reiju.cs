using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-069 温思默克·丽久（角色）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数，且我方手牌不多于 5 张的场合，抽取 2 张卡牌。
///
/// 说明：
///   - "我方场上咚数" / "对方场上咚数" 取费用区咚总数（TotalDonInCostArea）。
///   - 两个条件均在登场结算时刻判定，满足则抽 2 张。
/// </summary>
public class OP06_069_Reiju : IScriptedEffect
{
    public string CardNumber => "OP06-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea <= opp.TotalDonInCostArea && me.Hand.Count <= 5)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        }

        return Task.CompletedTask;
    }
}
