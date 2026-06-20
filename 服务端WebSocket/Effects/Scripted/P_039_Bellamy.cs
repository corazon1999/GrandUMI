using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-039 贝拉密（角色）
/// 【流放】纯关键词，由引擎处理。
/// 【咚!!×2】我方生命卡牌为 0 张的场合，此角色的力量 +2000。
///   → 持续条件力量加成，用 ContinuousEffect 注册（贴咚≥2 且我方生命=0 时生效）。
/// </summary>
public class P_039_Bellamy : IScriptedEffect
{
    public string CardNumber => "P-039";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2 &&
                s.Players[owner].LifeCount == 0,
        });
        return Task.CompletedTask;
    }
}
