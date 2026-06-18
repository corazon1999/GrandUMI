using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-066 托尼托尼·乔巴（角色）
/// 【阻挡者】（由引擎处理关键词）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 仅实现【登场时】主动效果；【阻挡者】关键词由引擎处理。
///   - 双方咚!! 张数相对比较用 me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 追加休息咚!! 用 RefreshDonFromDeck(me, 1, DonState.Rest)。
/// </summary>
public class OP07_066_TonyTonyChopper : IScriptedEffect
{
    public string CardNumber => "OP07-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
