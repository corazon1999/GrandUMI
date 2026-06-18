using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-019 巴尔托洛梅奥（角色）
/// 【阻挡者】（关键词由引擎处理）
/// 【咚!!×2】【对方的回合中】此角色的力量+3000。
///
/// 实现说明：
///   - 关键词【阻挡者】由引擎处理；本脚本实现持续力量修正。
///   - 用 ContinuousEffect 注册（登场时）：仅自身，对方回合中且自身被赋予咚≥2 时 +3000。
/// </summary>
public class OP01_019_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "OP01-019";

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
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
