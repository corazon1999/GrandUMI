using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-094 伊斯卡（角色 / 地 / 海军，cost3 power4000）
/// 【咚!!×1】【每回合1次】当此角色通过战斗 KO 对方的角色时，将此角色转为活跃状态。
///
/// 实现说明：
///   - "通过战斗 KO 对方角色"=OnAnyCharKOd（reason=="battle"）。
///   - 校验：被KO者属对方；且本次战斗的攻击者为本卡（CurrentBattle.AttackerCardId==self.Id，
///     战斗KO派发时 CurrentBattle 仍存在）。
///   - 【咚!!×1】=本卡被赋予中的咚!! ≥1；每回合1次。满足后 ActivateCard 使自身活跃。
/// </summary>
public class OP02_094_Hina : IScriptedEffect
{
    public string CardNumber => "OP02-094";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAnyCharKOd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // reason 必须为战斗
        var reason = ctx.Vars.TryGetValue("reason", out var r) ? r as string : null;
        if (reason != "battle") return Task.CompletedTask;

        // 被 KO 者必须属于对方
        var koOwner = ctx.Vars.TryGetValue("owner", out var o) && o is int oi ? oi : -1;
        if (koOwner != 1 - ctx.OwnerIndex) return Task.CompletedTask;

        // 本次战斗的攻击者必须是本卡
        var b = ctx.State.CurrentBattle;
        if (b is null || b.AttackerCardId != ctx.Source.Id) return Task.CompletedTask;

        // 【咚!!×1】发动条件：本卡被赋予中的咚!! ≥1
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

        var key = ctx.Source.Info.Number + "-koactivate:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.ActivateCard(ctx.Source);
        return Task.CompletedTask;
    }
}
