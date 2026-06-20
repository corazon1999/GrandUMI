using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-050 山智（角色）
/// 【阻挡者】纯关键词，由引擎处理。
/// 【咚!!×1】【我方的回合中】我方手牌不多于 3 张的场合，此角色的力量 +4000。
///   → 持续条件力量加成，用 ContinuousEffect 注册（贴咚≥1 且我方回合中 且手牌≤3 时生效）。
/// </summary>
public class P_050_Sanji : IScriptedEffect
{
    public string CardNumber => "P-050";

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
            PowerDelta = 4000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].Hand.Count <= 3,
        });
        return Task.CompletedTask;
    }
}
