using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-036 Mr.2·盆·岁末(本萨姆)（角色，绿，力量1000）
/// 【登场时】将对方最多1张费用不高于4的角色转为休息状态。（由 DSL OP16.json 的 OnEnterField 处理）
/// 【攻击时】本回合中，此角色原本的力量变为与对方领袖力量相同。（本脚本处理）
///
/// 实现：
///   - 引擎分发按 trigger 粒度「脚本优先、否则退回 DSL」：本脚本仅 HandlesTrigger(OnAttackDeclare)，
///     OnEnterField 仍退回 DSL，无双重触发。
///   - 无门槛（区别于 OP16-055 的【咚×1】）；用 OriginalPowerOverride 把"原本力量"本回合性覆盖为
///     对方领袖当前力量，回合末由 TurnEngine 清除。本卡自身被赋予的咚加成仍叠加其上，符合
///     "原本力量变为X"语义。
/// </summary>
public class OP16_036_Bentham : IScriptedEffect
{
    public string CardNumber => "OP16-036";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var oppLeader = ctx.State.Players[oppIdx].Leader;
        ctx.Source.OriginalPowerOverride = ctx.State.CurrentPowerOf(oppIdx, oppLeader);
        return Task.CompletedTask;
    }
}
