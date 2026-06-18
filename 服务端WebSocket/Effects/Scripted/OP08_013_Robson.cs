using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-013 罗布松（角色 / 炎 / 动物·铁桶王国）
/// 【咚!!×2】此角色获得【速攻】效果。
///
/// 实现说明：
///   - 条件性持续赋予关键词 → ContinuousEffect.GrantKeyword="速攻"，
///     Predicate 限定为本卡自身且其被赋予中的咚!! ≥ 2 时生效（OnEnterField 注册，离场自动清理）。
/// </summary>
public class OP08_013_Robson : IScriptedEffect
{
    public string CardNumber => "OP08-013";

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
            GrantKeyword = "速攻",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
