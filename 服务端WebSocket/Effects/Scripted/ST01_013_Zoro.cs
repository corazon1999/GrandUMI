using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST01-013 罗罗诺亚·佐罗（角色）
/// 【咚!!×1】此角色的力量+1000。（持续：自身被赋予≥1张咚!! 时生效）
/// 实现：登场时注册自身持续力量修正。effectTags 已补 OnEnterField 以触发注册。
/// </summary>
public class ST01_013_Zoro : IScriptedEffect
{
    public string CardNumber => "ST01-013";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, side, card) => card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
