using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-012 蒙奇·D·路飞（8费角色）
/// 我方场上存在费用为10或更高的角色的场合，此角色获得【速攻】效果。（持续 GrantKeyword）
/// </summary>
public class ST14_012_Luffy : IScriptedEffect
{
    public string CardNumber => "ST14-012";
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
            GrantKeyword = "速攻",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Characters.Any(c => c.Info.Cost >= 10),
        });
        return Task.CompletedTask;
    }
}
