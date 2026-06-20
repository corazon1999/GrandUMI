using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST21-002 撒谎布（角色）
/// 【咚!!×2】【对方的回合中】此角色的力量+2000。（持续）
/// </summary>
public class ST21_002_Usopp : IScriptedEffect
{
    public string CardNumber => "ST21-002";
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
            Predicate = (s, side, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2 &&
                s.CurrentTurnPlayer != owner,
        });
        return Task.CompletedTask;
    }
}
