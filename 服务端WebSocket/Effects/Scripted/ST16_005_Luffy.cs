using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST16-005 蒙奇·D·路飞（角色）
/// 我方的“乌塔”处于休息状态的场合，此角色的力量+1000。（持续）
/// </summary>
public class ST16_005_Luffy : IScriptedEffect
{
    public string CardNumber => "ST16-005";
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
            PowerDelta = 1000,
            Predicate = (s, side, card) =>
                card.Id == selfId &&
                s.Players[owner].Characters.Any(c => c.Info.NameIs("乌塔") && c.IsTapped),
        });
        return Task.CompletedTask;
    }
}
