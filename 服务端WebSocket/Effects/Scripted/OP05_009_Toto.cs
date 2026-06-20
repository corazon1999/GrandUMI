using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-009 多多（角色）
/// 【登场时】我方领袖力量不高于 0 的场合，抽取 1 张卡牌。
///
/// 实现说明：
///   - "领袖力量不高于 0" 用 ctx.State.CurrentPowerOf 取含持续修正后的实时力量判定，
///     避免依赖 DSL 仅有 leaderPowerGte 的限制。
/// </summary>
public class OP05_009_Toto : IScriptedEffect
{
    public string CardNumber => "OP05-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.State.CurrentPowerOf(ctx.OwnerIndex, me.Leader) <= 0)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }

        return Task.CompletedTask;
    }
}
