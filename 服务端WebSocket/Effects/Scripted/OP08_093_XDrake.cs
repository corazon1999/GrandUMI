using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-093 X·德雷克（角色 / 地 / 海军·德雷克海盗团·百兽海盗团）
/// 【咚!!×1】此角色的费用+2。
///
/// 实现说明：
///   - 条件性持续费用修正 → ContinuousEffect.CostDelta=2，
///     Predicate 限定为本卡自身且其被赋予中的咚!! ≥ 1 时生效（OnEnterField 注册）。
/// </summary>
public class OP08_093_XDrake : IScriptedEffect
{
    public string CardNumber => "OP08-093";

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
            CostDelta = 2,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
