using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP16-071 波头之仁王：登场时可弃1张手牌追加1张休息咚；KO时无成本追加1张休息咚。</summary>
public sealed class OP16_071_BenevolentKing : IScriptedEffect
{
    public string CardNumber => "OP16-071";
    public bool HandlesTrigger(EffectTrigger trigger) =>
        trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
            return;
        }

        if (me.Hand.Count == 0 ||
            !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "波头之仁王【登场时】：丢弃1张手牌，追加最多1张休息咚!!？"))
            return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张手牌", me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = me.Hand.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
