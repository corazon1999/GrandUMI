using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-094 龙马（角色 / 地 4 费 6000，和之国·恐怖之船海盗团）
/// 【咚!!×1】此角色获得【双重攻击】效果。
///
/// 实现：条件持续 GrantKeyword="双重攻击"（规范 13.1），在 OnEnterField 注册。
///   Predicate：本卡自身、被赋予中的咚!! ≥1 时生效。
/// </summary>
public class OP10_094_Ryuma : IScriptedEffect
{
    public string CardNumber => "OP10-094";

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
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
