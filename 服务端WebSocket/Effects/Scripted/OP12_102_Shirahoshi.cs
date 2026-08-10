using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-102 白星（角色）
/// 【对方的回合中】我方场上没有其他原本费用为2的"白星"的场合，
///   我方所有拥有《海王类》特征的角色力量+2000。
///   —— 通过 ContinuousEffect 注册：仅对方回合、且我方场上无其他原本费用2的"白星"时生效，
///      作用于我方所有含《海王类》特征的角色。
///
/// 我方原本费用≤6的角色因对方效果将要离场时，可以改为将生命区最上方1张翻至正面，使该角色不离场。
/// 效果KO和非KO效果离场共用统一守护逻辑；生命为空或生命顶已经正面朝上时无法支付。
/// </summary>
public class OP12_102_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "OP12-102";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope
                {
                    Side = 0,
                    IncludeLeader = false,
                    IncludeCharacters = true,
                    Filter = c => c.Info.HasKeyword("海王类"),
                },
                PowerDelta = 2000,
                Predicate = (s, sideIdx, card) =>
                    s.CurrentTurnPlayer != owner &&
                    !s.Players[owner].Characters.Any(c =>
                        c.Id != selfId && c.Info.Cost == 2 && c.Info.Name.Contains("白星")),
            });
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - owner)) return;
        var me = ctx.State.Players[owner];
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victimOwner != owner || victim is null || victim.Info.Cost > 6) return;
        if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;

        if (!await ctx.Prompts.ConfirmOptional(owner,
            "白星：将生命区最上方1张翻至正面，使该费用不高于6的角色不离场？")) return;
        AtomicOps.FlipTopLifeFaceUp(me);
        ctx.State.MarkPreventEffectLeaveBatch(owner, victim.Id,
            card => card.Info.Cost <= 6, isKoReplacement: !nonKoLeave);

    }
}
