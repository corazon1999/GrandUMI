using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-011 巴尔托洛梅奥（角色 / 炎）
/// 【咚!!×2】此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件性持续赋予关键词，用 ContinuousEffect.GrantKeyword="阻挡者" 注册（登场时）。
///   - Predicate 限定仅本卡自身、且自身被赋予中的咚!!≥2 时授予。
///   - 贴咚数用 PlayerState.AttachedDonCount(Guid) 取。
/// </summary>
public class OP14_011_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "OP14-011";

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
                c.Id == selfId && sideIdx == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
