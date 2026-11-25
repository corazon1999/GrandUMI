using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>龙胆：对方回合KO时可补最多1生命，之后必定受到1点效果伤害。</summary>
public sealed class OP14_115_Rindou : IScriptedEffect
{
    public string CardNumber => "OP14-115";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count > 0 && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "龙胆：将卡组最上方1张加入生命区？之后无论是否加入，均受到1点伤害。"))
            AtomicOps.AddLifeFromDeckTop(me, 1);
        await LifeRevealManager.DealDamageToLeader(ctx.State, ctx.Prompts, ctx.OwnerIndex, 1,
            effectDamage: true, engine: ctx.Engine);
    }
}
