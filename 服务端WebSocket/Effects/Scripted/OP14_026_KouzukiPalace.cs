using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-026 光月御殿（角色）
/// 【对方的回合中】此角色处于休息状态的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 纯持续/静态力量修正，用 ContinuousEffect 在【登场时】注册。
///   - Predicate：仅在"非我方回合（即对方回合中）"且本卡处于休息状态时生效。
///   - 来源卡离场时引擎自动清理；重复登场前先去重避免叠加。
/// </summary>
public class OP14_026_KouzukiPalace : IScriptedEffect
{
    public string CardNumber => "OP14-026";

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
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Id == selfId,
            },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                card.IsTapped,
        });

        return Task.CompletedTask;
    }
}
