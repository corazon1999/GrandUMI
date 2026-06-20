using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST13 萨波三兄弟 2 费版（萨波/艾斯/路飞）共享：
/// 【启动主要】可以将此角色放置到废弃区：公开我方生命区最上方1张，且为费用5的“[本系名]”时可将其登场；
///   登场的场合，直到下个对方回合结束，我方最多1张领袖力量+2000。
/// </summary>
internal static class St13Brother
{
    public static async Task Run(EffectContext ctx, string targetName)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0) return;
        // 成本：自身入废弃
        BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source);
        var top = me.LifeArea[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { top.Info.Number });
        if (!(top.Info.NameIs(targetName) && top.Info.Cost == 5 && top.Info.Kind == CardKind.Character)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, $"将生命区顶的「{top.Info.Name}」登场？")) return;
        AtomicOps.PlayFromLifeFree(ctx.State, ctx.OwnerIndex, top);
        // 登场成功 → 我方领袖+2000（近似“直到下个对方回合结束”，用持久修正，回合末清理）
        AtomicOps.AddPowerPersistent(me.Leader, 2000);
    }
}

/// <summary>ST13-007 萨波（2费）</summary>
public class ST13_007_Sabo : IScriptedEffect
{
    public string CardNumber => "ST13-007";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => St13Brother.Run(ctx, "萨波");
}

/// <summary>ST13-010 波特夹斯·D·艾斯（2费）</summary>
public class ST13_010_Ace : IScriptedEffect
{
    public string CardNumber => "ST13-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => St13Brother.Run(ctx, "波特夹斯·D·艾斯");
}

/// <summary>ST13-014 蒙奇·D·路飞（2费）</summary>
public class ST13_014_Luffy : IScriptedEffect
{
    public string CardNumber => "ST13-014";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx) => St13Brother.Run(ctx, "蒙奇·D·路飞");
}
