using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-001 罗罗诺亚·佐罗（领航）
/// 【咚!!×1】【我方的回合中】我方的所有角色力量+1000。
///
/// 实现说明：
///   - 纯持续力量光环，用 ContinuousEffect 注册（领袖在 OnGameStart 时机注册一次）。
///   - 【咚!!×1】= 领袖被赋予中的咚!! ≥1；【我方的回合中】= s.CurrentTurnPlayer == owner。
///   - Scope 仅含角色（不含领袖），Side=0（我方）。
/// </summary>
public class OP01_001_Zoro : IScriptedEffect
{
    public string CardNumber => "OP01-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

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
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
