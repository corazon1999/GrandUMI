using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-095 鬼蜘蛛（角色）
/// 场上存在费用为 0 的角色的场合，此角色获得【流放】效果。
///
/// 实现说明：
///   - 条件赋予关键词【流放】用 ContinuousEffect.GrantKeyword（引擎已消费【流放】）。
///   - Predicate：当(双方)场上任一角色当前费用为 0 时，对本卡授予【流放】。
///   - 在 OnEnterField 注册，离场由引擎自动清理；RemoveAll 去重。
/// </summary>
public class OP02_095_Onigumo : IScriptedEffect
{
    public string CardNumber => "OP02-095";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "流放",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                (s.Players[0].Characters.Any(c => s.CurrentCostOf(0, c) == 0) ||
                 s.Players[1].Characters.Any(c => s.CurrentCostOf(1, c) == 0)),
        });

        return Task.CompletedTask;
    }
}
