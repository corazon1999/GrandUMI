using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-085 吉扎斯·巴杰斯（角色 / 地 5 费 6000，德莱斯罗兹·黑胡子海盗团）
/// 【咚!!×1】我方废弃区中有8张或更多卡牌的场合，此角色获得【速攻】效果。
///
/// 实现：条件持续 GrantKeyword="速攻"（规范 13.1），在 OnEnterField 注册。
///   Predicate：本卡自身、被赋予中的咚!! ≥1、且我方废弃区 ≥8 张时生效。
/// </summary>
public class OP10_085_JesusBurgess : IScriptedEffect
{
    public string CardNumber => "OP10-085";

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
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].Trash.Count >= 8,
        });
        return Task.CompletedTask;
    }
}
