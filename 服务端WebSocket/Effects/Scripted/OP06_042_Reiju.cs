using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-042 温思默克·丽久（领航）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，抽取1张卡牌。
///
/// 实现说明：
///   - 使用反应式 watcher 触发 OnDonReturnedToDeck（咚!!放回咚!!卡组时派发，payload: ctx.Vars["count"]）。
///   - 仅在我方回合中生效，且每回合1次。
/// </summary>
public class OP06_042_Reiju : IScriptedEffect
{
    public string CardNumber => "OP06-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 抽取1张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
