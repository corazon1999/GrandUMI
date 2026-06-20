using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-061 盖德（领航 / 水·暗）
/// 【咚!!×1】【我方的回合中】【每回合1次】当对方的角色被KO时，从咚‼卡组中追加最多1张活跃状态的咚‼。
///
/// 实现：监听 OnAnyCharKOd（战斗/效果KO 均派发，payload owner=被KO卡所属方）。
/// 仅我方回合、被KO者为对方角色、此领袖被赋予咚≥1、每回合1次时，从咚卡组补1张活跃咚。
/// </summary>
public class OP01_061_Gedatsu : IScriptedEffect
{
    public string CardNumber => "OP01-061";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAnyCharKOd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;   // 仅我方回合
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != 1 - ctx.OwnerIndex) return Task.CompletedTask;                       // 须为对方角色被KO
        if (me.AttachedDonCount(self.Id) < 1) return Task.CompletedTask;                  // 咚!!×1

        var key = "OP01-061-kod" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;                     // 每回合1次
        me.TurnOnceUsed.Add(key);

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        return Task.CompletedTask;
    }
}
