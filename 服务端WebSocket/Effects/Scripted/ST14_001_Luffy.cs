using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-001 蒙奇·D·路飞（领航）
/// 【咚!!×1】我方所有角色费用+1。我方场上存在费用为8或更高的角色的场合，此领袖的力量+1000。（持续，OnGameStart 注册）
/// </summary>
public class ST14_001_Luffy : IScriptedEffect
{
    public string CardNumber => "ST14-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var leaderId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == leaderId.ToString());
        // 【咚!!×1】我方所有角色费用+1
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 1,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id != leaderId &&
                s.Players[owner].AttachedDonCount(leaderId) >= 1,
        });
        // 有费用≥8角色时此领袖力量+1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == leaderId && s.Players[owner].Characters.Any(c => c.Info.Cost >= 8),
        });
        return Task.CompletedTask;
    }
}
