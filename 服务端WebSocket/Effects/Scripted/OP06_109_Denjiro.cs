using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-109 传次郎（角色 / 光 5 费 6000，和之国/赤鞘九人男）
/// 【咚!!×2】对方生命卡牌不多于3张的场合，此角色不会因效果而被KO。
///
/// 实现说明：
///   - 持续"不会因效果被KO"用 ContinuousEffect.KoGuard = "effect" 实现（在 OnEnterField 注册）。
///   - 条件：此角色被赋予中的咚!! ≥2，且对方生命牌数量 ≤3，二者同时满足时生效。
///   - 来源卡离场时引擎自动清理；重复登场前先去重避免叠加。
/// </summary>
public class OP06_109_Denjiro : IScriptedEffect
{
    public string CardNumber => "OP06-109";

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
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2 &&
                s.Players[1 - owner].LifeArea.Count <= 3,
        });

        return Task.CompletedTask;
    }
}
