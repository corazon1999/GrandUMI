using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST09-004 盖德（角色）
/// 【咚!!×1】我方生命卡牌不多于2张的场合，此角色在战斗中不会被KO。（持续 KoGuard：battle）
/// </summary>
public class ST09_004_Kaido : IScriptedEffect
{
    public string CardNumber => "ST09-004";
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
            KoGuard = "battle",
            Predicate = (s, side, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].LifeCount <= 2,
        });
        return Task.CompletedTask;
    }
}
