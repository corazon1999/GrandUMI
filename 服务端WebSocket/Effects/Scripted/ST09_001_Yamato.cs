using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST09-001 大和（领航）
/// 【咚!!×1】【对方的回合中】我方生命卡牌不多于2张的场合，此领袖的力量+1000。（持续，OnGameStart 注册）
/// </summary>
public class ST09_001_Yamato : IScriptedEffect
{
    public string CardNumber => "ST09-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var leaderId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == leaderId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == leaderId &&
                s.Players[owner].AttachedDonCount(leaderId) >= 1 &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].LifeCount <= 2,
        });
        return Task.CompletedTask;
    }
}
