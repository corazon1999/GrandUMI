using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>修罗的条件速攻独立于登场时返咚抽牌，避免选择性登场无效误伤持续能力。</summary>
public sealed class OP15_067_Shura : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP15-067";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var sourceId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "速攻",
            Predicate = (state, side, card) => side == owner && card.Id == sourceId
                && state.Players[owner].TotalDonInCostArea <= 6,
        });
        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField && await AtomicOps.PromptReturnDonToDeck(ctx, 1))
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }
}
