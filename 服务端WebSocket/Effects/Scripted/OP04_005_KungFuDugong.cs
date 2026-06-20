using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-005 功夫海牛（角色 / 炎 / 动物・阿拉巴斯坦王国）
/// 我方场上存在除此角色以外的"功夫海牛"的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件性静态获得关键词 → ContinuousEffect.GrantKeyword（Wave1 13.1）。
///   - 在 OnEnterField 注册；Predicate 仅授予本卡自身，且需我方场上存在另一张同名"功夫海牛"。
/// </summary>
public class OP04_005_KungFuDugong : IScriptedEffect
{
    public string CardNumber => "OP04-005";

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
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].Characters.Any(o => o.Id != selfId && o.MatchesName("功夫海牛")),
        });

        return Task.CompletedTask;
    }
}
