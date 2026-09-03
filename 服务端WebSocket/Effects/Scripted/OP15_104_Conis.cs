using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP15-104 柯妮丝：登场时抽2弃2；生命触发抽2弃1。</summary>
public sealed class OP15_104_Conis : IScriptedEffect
{
    public string CardNumber => "OP15-104";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField
            && me.LifeArea.Count >= ctx.State.Players[1 - ctx.OwnerIndex].LifeArea.Count) return;

        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
        int discardCount = ctx.Trigger == EffectTrigger.OnLifeRevealTrigger ? 1 : 2;
        int required = Math.Min(discardCount, me.Hand.Count);
        if (required == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            $"选择{required}张手牌丢弃",
            me.Hand.Select(card => card.Id.ToString()).ToList(), required, required);
        foreach (var card in me.Hand.Where(card => chosen.Contains(card.Id.ToString())).Take(required).ToList())
            AtomicOps.DiscardHand(me, card);
    }
}
