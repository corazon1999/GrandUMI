using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-009 弗兰奇（角色）
/// 【咚!!×1】【对方的回合中】我方场上存在费用为6或更高的角色的场合，此角色不会因对方的效果而被KO，且力量+2000。（持续）
/// </summary>
public class ST14_009_Franky : IScriptedEffect
{
    public string CardNumber => "ST14-009";
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
            PowerDelta = 2000,
            KoGuard = "effect",
            Predicate = (s, side, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Characters.Any(c => c.Info.Cost >= 6),
        });
        return Task.CompletedTask;
    }
}
