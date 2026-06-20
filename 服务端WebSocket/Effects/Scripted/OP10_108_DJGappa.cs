using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-108 打碟人·阿保（角色 / 光 1 费 1000，超新星·广播海盗团）
/// 我方场上存在"打碟人·阿保"以外的拥有《超新星》特征的黄色（光）角色的场合，此角色获得【阻挡者】效果。
///
/// 实现：条件持续 GrantKeyword="阻挡者"（规范 13.1），在 OnEnterField 注册。
///   Predicate：本卡自身，且我方场上存在另一张《超新星》且为光色的角色（排除自身）时生效。
/// </summary>
public class OP10_108_DJGappa : IScriptedEffect
{
    public string CardNumber => "OP10-108";

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
                s.Players[owner].Characters.Any(c =>
                    c.Id != selfId && c.Info.HasKeyword("超新星") && c.Info.ColorList.Contains("黄")),
        });
        return Task.CompletedTask;
    }
}
