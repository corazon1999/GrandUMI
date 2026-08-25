using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-085 乌鸦：我方领袖拥有《革命军》特征时，此角色费用 +3。
/// 攻击时弃牌效果仍由 OP12.json 负责，本脚本只登记动态持续费用。
/// </summary>
public sealed class OP12_085_Karasu : IScriptedEffect
{
    public string CardNumber => "OP12-085";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var sourceId = ctx.Source.Id;
        var owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
            },
            CostDelta = 3,
            Predicate = (state, side, card) =>
                side == owner
                && card.Id == sourceId
                && state.Players[owner].Leader.Info.HasKeyword("革命军"),
        });
        return Task.CompletedTask;
    }
}
