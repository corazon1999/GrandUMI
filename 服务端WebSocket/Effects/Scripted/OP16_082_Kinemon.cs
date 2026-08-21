using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-082 Kin'emon.
/// This character gains +3 cost continuously. Its OnEnterField search remains
/// implemented by the OP16 DSL definition.
/// </summary>
public sealed class OP16_082_Kinemon : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP16-082";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = card => card.Id == selfId,
            },
            CostDelta = 3,
            Predicate = (_, side, card) =>
                side == owner && card.Id == selfId && !card.IsEffectsNullified,
        });

        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        await RegisterFieldStatic(ctx);

        await Dsl.DslInterpreter.TryResolve(ctx);
    }
}
