using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-098 蒙奇·D·路飞（领航）
/// 置换：当我方《空岛》原本力量 ≥6000 的角色因对方将要离场时，
///   可将生命区最上方 1 张加入手牌，使该角色不离场。
/// 效果KO和非KO效果离场均通过守护触发处理。
/// </summary>
public class OP15_098_Luffy : IScriptedEffect
{
    public string CardNumber => "OP15-098";
    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victimOwner != ctx.OwnerIndex || victim is null ||
            !victim.Info.HasKeyword("空岛") || victim.Info.Power < 6000) return;
        if (me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞：将生命区最上方1张加入手牌，使该《空岛》角色不离场？");
        if (!use) return;
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        top.IsLifeFaceUp = false;
        me.Hand.Add(top);
        ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victim.Id,
            card => card.Info.HasKeyword("空岛") && card.Info.Power >= 6000, isKoReplacement: !nonKoLeave);
    }
}
