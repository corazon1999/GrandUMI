using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST01-004 山智（角色）
/// 持续：【咚!!×2】此角色被赋予中的咚!! ≥2 时，获得【速攻】。
///   用 ContinuousEffect.GrantKeyword + Predicate（按 AttachedDonCount 判定）实现，
///   登场时注册，离场由引擎清理；重复登场前先去重避免叠加。
/// </summary>
public class ST01_004_Sanji : IScriptedEffect
{
    public string CardNumber => "ST01-004";

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
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
