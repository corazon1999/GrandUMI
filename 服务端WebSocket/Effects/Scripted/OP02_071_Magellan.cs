using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-071 麦哲伦（领袖 / 暗 / 因佩尔地狱，power5000）
/// 【我方的回合中】【每回合1次】场上的咚!!放回咚!!卡组时，本回合中，此领袖力量 +1000。
///
/// 实现说明：
///   - "咚!!放回咚!!卡组时"=OnDonReturnedToDeck（Wave2 watcher）。
///   - 限我方回合（CurrentTurnPlayer==owner）+每回合1次；本回合 +1000 用 AddPowerThisTurn。
/// </summary>
public class OP02_071_Magellan : IScriptedEffect
{
    public string CardNumber => "OP02-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        var key = ctx.Source.Info.Number + "-donreturn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.AddPowerThisTurn(ctx.Source, 1000);
        return Task.CompletedTask;
    }
}
