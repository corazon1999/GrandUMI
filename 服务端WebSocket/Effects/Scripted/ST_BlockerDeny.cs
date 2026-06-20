using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST01-002 撒谎布（角色）
/// 【咚!!×2】【攻击时】本次战斗中，对方力量5000或更高的角色不能发动【阻挡者】效果。
/// 实现：对所有当前力量≥5000的对方角色施加 CannotBeBlocker（本次战斗）。
/// </summary>
public class ST01_002_Usopp : IScriptedEffect
{
    public string CardNumber => "ST01-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 2) return Task.CompletedTask;
        int oppIdx = 1 - ctx.OwnerIndex;
        foreach (var c in ctx.State.Players[oppIdx].Characters)
            if (ctx.State.CurrentPowerOf(oppIdx, c) >= 5000)
                AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST01-012 蒙奇·D·路飞（角色）
/// 【速攻】【咚!!×2】【攻击时】本次战斗中，对方不能发动【阻挡者】效果。
/// 实现：对所有对方角色施加 CannotBeBlocker（本次战斗）。
/// </summary>
public class ST01_012_Luffy : IScriptedEffect
{
    public string CardNumber => "ST01-012";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 2) return Task.CompletedTask;
        int oppIdx = 1 - ctx.OwnerIndex;
        foreach (var c in ctx.State.Players[oppIdx].Characters)
            AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        return Task.CompletedTask;
    }
}
