using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST29-003 卡古（角色）
/// 我方生命卡牌的张数不多于对方生命卡牌的张数的场合，此角色的力量+1000。（持续）
/// （【触发】KO 对方费用≤3 角色 由 DSL ST29.json 实现）
/// </summary>
public class ST29_003_Kaku : IScriptedEffect
{
    public string CardNumber => "ST29-003";
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
                card.Id == selfId &&
                s.Players[owner].LifeCount <= s.Players[1 - owner].LifeCount,
        });
        return Task.CompletedTask;
    }
}
