using GrandUMI.Effects.Dsl;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>库赞的对方效果离场免疫；启动主要继续复用原有成本及复活定义。</summary>
public sealed class OP10_082_Kuzan : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP10-082";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.ActivatedMain or EffectTrigger.PreKO or EffectTrigger.OnSelfWillLeaveField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var sourceId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId.ToString());
        // 底层移区也检查同一持续免疫，兼容未显式派发离场守护的历史脚本。
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeCharacters = true },
            LeaveGuard = "effect",
            Predicate = (_, side, card) => side == owner && card.Id == sourceId
                && EffectRuntime.CurrentActingSide == 1 - owner,
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (state, side, card) => side == owner && card.Id == sourceId
                && state.KOReason == "effect" && state.KOActingSide == 1 - owner,
        });
        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            await DslInterpreter.TryResolve(ctx);
            return;
        }
        // 选择角色的玩家不改变效果来源；战斗KO与己方成本仍可正常离场。
        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            if (ctx.State.KOReason == "effect" && ctx.State.KOActingSide == 1 - ctx.OwnerIndex)
                ctx.State.MarkPreventKO(ctx.Source.Id);
        }
        else if (ctx.Vars.TryGetValue("actingSide", out var rawSide) && rawSide is int actingSide && actingSide == 1 - ctx.OwnerIndex)
            ctx.State.PreventLeaveCardIds.Add(ctx.Source.Id);
    }
}
