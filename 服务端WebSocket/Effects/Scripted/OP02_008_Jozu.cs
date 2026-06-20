using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-008 乔兹（角色 / 炎 / 白胡子海盗团）
/// 【咚!!×1】我方生命卡牌不多于2张，且我方领袖拥有的特征中包含〈白胡子海盗团〉的场合，
/// 此角色获得【速攻】效果。
///
/// 实现说明：条件型持续关键词，用 ContinuousEffect.GrantKeyword="速攻" + Predicate 注册（Wave1）。
///   条件：被赋予的咚 ≥1、我方生命 ≤2、我方领袖含〈白胡子海盗团〉。仅作用于自身。
/// </summary>
public class OP02_008_Jozu : IScriptedEffect
{
    public string CardNumber => "OP02-008";

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
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].LifeCount <= 2 &&
                s.Players[owner].Leader.Info.HasKeyword("白胡子海盗团"),
        });

        return Task.CompletedTask;
    }
}
