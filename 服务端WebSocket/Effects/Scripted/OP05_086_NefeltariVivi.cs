using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-086 奈菲特·薇薇（角色 / 地 1 费 1000，阿拉巴斯坦王国）
/// 我方废弃区中有 10 张或更多卡牌的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件持续关键词，用 ContinuousEffect.GrantKeyword 注册（OnEnterField 注册，离场自动清理）。
///   - "废弃区 ≥10 张" 以我方 Trash.Count 计。
/// </summary>
public class OP05_086_NefeltariVivi : IScriptedEffect
{
    public string CardNumber => "OP05-086";

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
                s.Players[owner].Trash.Count >= 10,
        });

        return Task.CompletedTask;
    }
}
