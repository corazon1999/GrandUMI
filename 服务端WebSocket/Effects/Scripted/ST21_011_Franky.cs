using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST21-011 弗兰奇（角色）
/// 【咚!!×2】【对方的回合中】我方所有原本的力量不高于4000且拥有《草帽一伙》特征的角色力量+1000。（持续）
/// </summary>
public class ST21_011_Franky : IScriptedEffect
{
    public string CardNumber => "ST21-011";
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
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id != s.Players[owner].Leader.Id &&
                s.Players[owner].AttachedDonCount(selfId) >= 2 &&
                s.CurrentTurnPlayer != owner &&
                card.Info.Power <= 4000 &&
                card.Info.HasKeyword("草帽一伙"),
        });
        return Task.CompletedTask;
    }
}
