using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-044 萨波（角色）
/// 【咚!!×1】我方手牌不多于 4 张的场合，此角色的力量 +2000。
///   → 持续条件力量加成，用 ContinuousEffect 注册（贴咚≥1 且我方手牌≤4 时生效）。
/// </summary>
public class P_044_Sabo : IScriptedEffect
{
    public string CardNumber => "P-044";

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
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].Hand.Count <= 4,
        });
        return Task.CompletedTask;
    }
}
