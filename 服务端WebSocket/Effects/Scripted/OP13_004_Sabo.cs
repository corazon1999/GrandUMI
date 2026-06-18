using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-004 萨波（领航 / 炎・地 / 德莱斯罗兹・革命军）
/// 我方生命卡牌为4张或更多的场合，此领袖的力量-1000。
/// 【咚!!×1】我方场上存在费用为8或更高的角色的场合，我方的领袖和所有角色力量+1000。
///
/// 实现说明：
///   - 两条都是纯持续力量修正，用 ContinuousEffect 注册，OnGameStart（领袖永续）。
///   - 第一条：Scope 仅自身领袖，PowerDelta=-1000，Predicate 判我方生命≥4。
///   - 第二条：【咚!!×1】= 至少有1张被赋予中的咚（AttachedDonCount(self)>=1 近似——
///     OPCG【咚!!×N】指领袖被赋予 N 张咚）；且我方场上存在费用≥8角色时，
///     我方领袖+全体角色 +1000，PowerDelta=1000 作用于同方领袖与角色。
/// </summary>
public class OP13_004_Sabo : IScriptedEffect
{
    public string CardNumber => "OP13-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 生命≥4 → 此领袖 -1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = -1000,
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].LifeCount >= 4,
        });

        // 【咚!!×1】且存在费用≥8角色 → 我方领袖与全体角色 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner
                && s.Players[owner].AttachedDonCount(selfId) >= 1
                && s.Players[owner].Characters.Any(ch => ch.Info.Cost >= 8),
        });

        return Task.CompletedTask;
    }
}
