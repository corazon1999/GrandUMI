using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-050 波比·凡克（角色）
/// 持续：我方场上存在“凯利·凡克”的场合，此角色的力量 +3000。
///   （ContinuousEffect 注册，引擎按条件累加。）
/// </summary>
public class OP15_050_BobbyFunk : IScriptedEffect
{
    public string CardNumber => "OP15-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].Characters.Any(c => c.MatchesName("凯利·凡克")),
        });

        return Task.CompletedTask;
    }
}
