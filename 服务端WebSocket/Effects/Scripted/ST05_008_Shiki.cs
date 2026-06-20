using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST05-008 西奇（角色）
/// 我方场上存在8张或更多咚!!的场合，此角色在战斗中不会被KO。（持续 KoGuard：battle）
/// </summary>
public class ST05_008_Shiki : IScriptedEffect
{
    public string CardNumber => "ST05-008";
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
                card.Id == selfId && s.Players[owner].TotalDonInCostArea >= 8,
        });
        return Task.CompletedTask;
    }
}
