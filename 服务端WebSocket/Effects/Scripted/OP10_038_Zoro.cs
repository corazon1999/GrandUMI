using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-038 罗罗诺亚·佐罗（角色）
/// 【对方的回合中】我方场上存在 2 张或更多处于休息状态的角色的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 纯持续/静态力量修正，用 ContinuousEffect 注册，登场时注册、离场时引擎自动清理。
///   - 条件：对方回合中（CurrentTurnPlayer != owner）且我方场上≥2 张休息角色。
/// </summary>
public class OP10_038_Zoro : IScriptedEffect
{
    public string CardNumber => "OP10-038";

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
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Characters.Count(c => c.IsTapped) >= 2,
        });

        return Task.CompletedTask;
    }
}
