using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-030 斯摩格（角色 / 风 / 班克禁区・海军）
/// 【流放】（此卡牌给予伤害的场合，不发动触发效果将该卡牌放置到废弃区）
/// 【启动主要】将我方最多 1 张咚!! 转为活跃状态。之后，本回合中，我方无法通过角色的效果将咚!! 转为活跃状态。
///
/// 实现范围：本脚本实现【启动主要】的收益——将我方最多 1 张休息状态的咚!! 转为活跃状态。
///
/// 简化点（未实现，无引擎通道）：
///   - 【流放】为触发效果替换（给予伤害时不发动触发、放入废弃区），引擎未实现该机制，作为纯关键词忽略。
///   - "之后，本回合中我方无法通过角色效果将咚!! 转为活跃"为持续性自我限制状态机，引擎无持续限制通道，
///     该部分为负面限制（非收益），按惯例省略，不影响本效果收益的正确性。
/// </summary>
public class OP10_030_Smoker : IScriptedEffect
{
    public string CardNumber => "OP10-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 将我方最多 1 张休息状态的咚!! 转为活跃状态
        var rest = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (rest is not null) rest.State = DonState.Active;

        return Task.CompletedTask;
    }
}
