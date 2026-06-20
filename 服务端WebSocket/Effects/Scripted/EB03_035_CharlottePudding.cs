using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-035 夏洛特·布玲（角色 / 暗 / 大妈海盗团，4 费 4000）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 【阻挡者】为纯关键词，脚本只实现【登场时】。
///   - "我方咚数 ≤ 对方咚数" 用双方 TotalDonInCostArea 直接比较。
///   - 满足条件 → RefreshDonFromDeck(me, 1, Rest)。
/// </summary>
public class EB03_035_CharlottePudding : IScriptedEffect
{
    public string CardNumber => "EB03-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea <= opp.TotalDonInCostArea)
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);

        return Task.CompletedTask;
    }
}
