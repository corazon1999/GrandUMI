using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-017 瓦亚（角色）
/// 【咚!!×1】我方领袖的力量为7000或更高且拥有《基德海盗团》特征的场合，此角色获得【速攻】效果。
/// 实现说明：条件性持续赋予自身【速攻】→ ContinuousEffect.GrantKeyword。
///   "【咚!!×1】"为发动条件：此角色被赋予的咚!! 合计 ≥1。
/// </summary>
public class OP09_017_Wire : IScriptedEffect
{
    public string CardNumber => "OP09-017";

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
            {
                if (c.Id != selfId) return false;
                var leader = s.Players[owner].Leader;
                return s.Players[owner].AttachedDonCount(selfId) >= 1
                    && leader.Info.HasKeyword("基德海盗团")
                    && s.CurrentPowerOf(owner, leader) >= 7000;
            },
        });

        return Task.CompletedTask;
    }
}
