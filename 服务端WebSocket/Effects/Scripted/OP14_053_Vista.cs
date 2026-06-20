using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-053 比斯塔（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】我方手牌不多于 7 张的场合，此角色原本的力量变为与我方领袖原本的力量相同。
///
/// 实现说明 / 简化点：
///   - "原本的力量变为与领袖原本力量相同" 是一条带条件的持续力量修正。由于此角色与领袖的
///     原本力量(Info.Power)在场上都不会变化，差值是一个固定常量，故用 ContinuousEffect.PowerDelta
///     = (领袖原本力量 - 自身原本力量) 注册即可达成"把自身力量拉到领袖原本力量"的等效结果。
///   - 条件：对方的回合中(CurrentTurnPlayer != owner) 且 我方手牌张数 ≤ 7。
///   - 登场时注册，离场时引擎自动清理；重复登场前先去重避免叠加。
///   - 【阻挡者】为纯关键词，由引擎处理，本脚本只实现持续力量部分。
/// </summary>
public class OP14_053_Vista : IScriptedEffect
{
    public string CardNumber => "OP14-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 把自身力量拉到领袖原本力量所需的固定差值
        int delta = me.Leader.Info.Power - self.Info.Power;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = delta,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Hand.Count <= 7,
        });

        return Task.CompletedTask;
    }
}
