using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-093 特拉法尔加·罗（角色 / 暗）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 仅实现【登场时】主动效果；【阻挡者】为关键词由引擎处理。
///   - 条件「我方场上咚张数 ≤ 对方场上咚张数」= me.TotalDonInCostArea ≤ opp.TotalDonInCostArea。
///   - 追加休息咚用 RefreshDonFromDeck(p, 1, DonState.Rest)。
/// </summary>
public class P_093_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "P-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea <= opp.TotalDonInCostArea)
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        }
        return Task.CompletedTask;
    }
}
