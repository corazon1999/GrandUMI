using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-066 加尔根（角色 / 紫 / 1费）
/// 我方废弃区中有4张或更多事件的场合，此角色获得【阻挡者】效果。
/// 实现：登场时注册持续 GrantKeyword="阻挡者"，Predicate 限定作用于自身且我方废弃区事件数≥4
///   （Scope 仅供显示、引擎只看 Predicate）。
/// </summary>
public class OP12_066_Gargon : IScriptedEffect
{
    public string CardNumber => "OP12-066";

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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].Trash.Count(c => c.Info.Kind == CardKind.Event) >= 4,
        });
        return Task.CompletedTask;
    }
}
