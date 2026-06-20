using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST06-004 斯摩格（角色）
/// 此角色不会因效果而被KO。【咚!!×1】场上存在费用为0的角色的场合，此角色获得【双重攻击】效果。（持续）
/// </summary>
public class ST06_004_Smoker : IScriptedEffect
{
    public string CardNumber => "ST06-004";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        // 不会因效果而被KO（持续）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) => card.Id == selfId,
        });
        // 【咚!!×1】场上存在费用0角色时获得双重攻击
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                (s.Players[0].Characters.Concat(s.Players[1].Characters)).Any(c => c.Info.Cost == 0),
        });
        return Task.CompletedTask;
    }
}
