using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST10-003 尤斯塔斯·基德（领航）
/// 【我方的回合中】我方生命卡牌为4张或更多的场合，此领袖的力量-1000。（持续，OnGameStart 注册）
/// （【攻击时】咚-1：此领袖+2000 由 DSL ST10.json 实现）
/// </summary>
public class ST10_003_Kid : IScriptedEffect
{
    public string CardNumber => "ST10-003";
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
            PowerDelta = -1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == leaderId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].LifeCount >= 4,
        });
        return Task.CompletedTask;
    }
}
