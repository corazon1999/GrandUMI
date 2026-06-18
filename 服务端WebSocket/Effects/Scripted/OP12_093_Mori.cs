using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-093 莫里（角色 / 黑 / 4费）
/// 我方领袖拥有《革命军》特征的场合，此角色的费用+4。
/// 实现：登场时注册持续费用修正（ContinuousEffect.CostDelta=4），
///   Predicate 限定作用于自身且我方领袖含《革命军》（Scope 仅供显示、引擎只看 Predicate）。
/// </summary>
public class OP12_093_Mori : IScriptedEffect
{
    public string CardNumber => "OP12-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 4,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("革命军"),
        });
        return Task.CompletedTask;
    }
}
