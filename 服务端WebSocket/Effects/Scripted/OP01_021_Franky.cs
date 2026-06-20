using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-021 弗兰奇（角色）
/// 【咚!!×1】此角色可以攻击对方活跃状态的角色。
///
/// 实现说明：
///   - Wave3：持续赋予关键词"可攻击活跃"，ActionValidator 据此放行攻击对方活跃角色。
///   - 条件【咚!!×1】= 自身被赋予咚≥1，用 ContinuousEffect.Predicate 表达；登场时注册。
/// </summary>
public class OP01_021_Franky : IScriptedEffect
{
    public string CardNumber => "OP01-021";

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
            GrantKeyword = "可攻击活跃",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
