using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-058 蒙布朗·库利凯特（角色 / 光 / 海盗猿山联合军）
/// 【咚!!×1】【我方的回合中】我方生命卡牌不多于 2 张的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 持续/条件力量修正用 ContinuousEffect 注册（OnEnterField 时机）。
///   - 条件：【咚!!×1】= 自身被赋予咚!! ≥1；【我方的回合中】= s.CurrentTurnPlayer == owner；我方生命≤2。
///   - 仅作用于自身：Predicate 限定 card.Id == self。
/// </summary>
public class EB01_058_Kricket : IScriptedEffect
{
    public string CardNumber => "EB01-058";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].LifeCount <= 2,
        });

        return Task.CompletedTask;
    }
}
