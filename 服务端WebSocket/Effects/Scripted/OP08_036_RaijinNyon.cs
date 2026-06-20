using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-036 电气 月神（事件）
/// 【主要】对方所有处于休息状态且费用不高于7的角色，在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 用 AtomicOps.PreventActivateNextReset 标记对方所有满足条件的休息角色，引擎在重置阶段尊重此标记。
///   - 【触发】"将对方最多1张角色转为休息状态"作为生命触发节由引擎单独处理，此处仅实现【主要】。
/// </summary>
public class OP08_036_RaijinNyon : IScriptedEffect
{
    public string CardNumber => "OP08-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var targets = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 7).ToList();
        foreach (var c in targets)
            AtomicOps.PreventActivateNextReset(c);

        return Task.CompletedTask;
    }
}
