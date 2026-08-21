using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-016 X·德雷克：对方回合中每回合一次，我方《超新星》角色因对方效果将要离场时，
/// 可以改为使我方领袖本回合力量-2000，让该角色不离场。
/// </summary>
public sealed class OP14_016_XDrake : IScriptedEffect
{
    public string CardNumber => "OP14-016";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.State.CurrentTurnPlayer != 1 - ctx.OwnerIndex) return;

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Characters.Contains(ctx.Source)) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var value) ? value as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var owner) && owner is int index ? index : -1;
        var victim = me.Characters.FirstOrDefault(card => card.Id.ToString() == victimId);
        if (victimOwner != ctx.OwnerIndex || victim is null || !victim.Info.HasKeyword("超新星")) return;

        string key = $"OP14-016-guard:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "X·德雷克：使我方领袖本回合力量-2000，让该《超新星》角色不离场？")) return;

        AtomicOps.AddPowerThisTurn(me.Leader, -2000);
        ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victim.Id,
            card => card.Info.HasKeyword("超新星"), isKoReplacement: !nonKoLeave);
        me.TurnOnceUsed.Add(key);
    }
}
