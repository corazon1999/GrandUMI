using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-004 卓夫（角色 / 炎 / 东海）
/// 【攻击时】直到下个对方的结束阶段结束时为止，我方领袖原本的力量变为7000。
///
/// 实现说明：
///   - 使用精确的跨回合原本力量变更，并记录施加方；来源角色之后离场也不会取消已经结算的限时效果。
///   - 由 TurnEngine 在施加方的下个对手结束阶段清除，不依赖回合编号推算。
/// </summary>
public class EB04_004_Dorry : IScriptedEffect
{
    public string CardNumber => "EB04-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var leader = me.Leader;
        AtomicOps.SetOriginalPowerUntilOppEnd(leader, 7000, ctx.OwnerIndex);

        return Task.CompletedTask;
    }
}
