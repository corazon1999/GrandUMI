using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-065 吉娜（角色）
/// 【登场时】我方领袖拥有《福克斯海盗团》特征，且我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 双方咚!! 张数相对比较用 me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 追加活跃咚!! 用 RefreshDonFromDeck(me, 1, DonState.Active)。
/// </summary>
public class OP07_065_Gina : IScriptedEffect
{
    public string CardNumber => "OP07-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Leader is null || !me.Leader.Info.HasKeyword("福克斯海盗团")) return Task.CompletedTask;
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        return Task.CompletedTask;
    }
}
