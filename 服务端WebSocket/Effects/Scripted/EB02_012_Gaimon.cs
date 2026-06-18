using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-012 盖蒙（角色 / 风 / 东海）
/// 我方场上存在"萨方克尔"的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件性持续关键词：用 ContinuousEffect.GrantKeyword="阻挡者"，Predicate 限定为自身且我方场上存在名为
///     "萨方克尔"的角色（领袖或角色）。在 OnEnterField 注册，离场后引擎自动清理。
/// </summary>
public class EB02_012_Gaimon : IScriptedEffect
{
    public string CardNumber => "EB02-012";

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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].Characters.Any(c => c.MatchesName("萨方克尔")),
        });

        return Task.CompletedTask;
    }
}
