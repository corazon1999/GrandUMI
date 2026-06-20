using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-037 弗兰奇（角色 / 暗 / 七水之城·草帽一伙，cost3 power4000）
/// 【登场时】/【攻击时】我方领袖拥有《草帽一伙》特征，且我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 两个时机共享同一条件与收益：领袖含《草帽一伙》且我方 TotalDonInCostArea ≤ 对方 → RefreshDonFromDeck(me,1,Rest)。
/// </summary>
public class EB02_037_Franky : IScriptedEffect
{
    public string CardNumber => "EB02-037";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("草帽一伙")) return Task.CompletedTask;
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
