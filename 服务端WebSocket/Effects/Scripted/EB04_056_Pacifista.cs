using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-056 和平主义者（角色 / 光 / 生物兵器・艾格赫德・海军）
/// 我方场上存在"杰丽·邦妮"，且我方生命卡牌为 0 张的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件赋予关键词用 ContinuousEffect.GrantKeyword = "阻挡者"（Wave1）。
///   - Predicate：仅作用于本卡自身（card.Id == selfId 且 sideIdx==owner），
///     且我方场上存在名为"杰丽·邦妮"的角色（或领袖），且我方生命为 0 张。
///   - OnEnterField 注册，离场由引擎自动清理。
/// </summary>
public class EB04_056_Pacifista : IScriptedEffect
{
    public string CardNumber => "EB04-056";

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
                s.Players[owner].LifeCount == 0 &&
                s.Players[owner].Characters.Any(c => c.MatchesName("杰丽·邦妮")),
        });

        return Task.CompletedTask;
    }
}
