using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-034 山智（角色 / 光 / 温思默克家，cost3 power4000）
/// 【咚!!×1】【我方的回合中】我方生命卡牌不多于 2 张的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 持续条件加力用 ContinuousEffect.PowerDelta=2000，仅作用本卡自身。
///   - 条件谓词：本卡被赋予中的咚!! ≥1（【咚!!×1】）、当前为我方回合、我方生命 ≤2。
/// </summary>
public class P_034_Sanji : IScriptedEffect
{
    public string CardNumber => "P-034";

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
                s.CurrentTurnPlayer == owner &&                       // 【我方的回合中】
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&     // 【咚!!×1】
                s.Players[owner].LifeArea.Count <= 2,                 // 生命 ≤2
        });
        return Task.CompletedTask;
    }
}
