using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-030 咚森（角色）
/// 【攻击时】我方领袖拥有《新鱼人海盗团》特征的场合，直到下个我方的回合开始时为止，
///   此角色在战斗中不会被KO，且力量+2000。之后，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 前提：我方领袖含特征《新鱼人海盗团》。
///   - "直到下个我方回合开始时不会被KO" 用 AddRestriction(CannotBeKOd,
///     UntilNextOpponentEndPhase)（引擎以"直到下个对方回合结束"近似"直到下个我方回合开始时"）。
///   - "力量+2000" 用 AddPowerThisTurn（攻击时本回合提升，覆盖本次战斗收益）。
///   - "生命区最上方 1 张加入手牌"：生命牌为私有区，手动 RemoveAt(0) 后加入手牌。
/// </summary>
public class OP06_030_Dosun : IScriptedEffect
{
    public string CardNumber => "OP06-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Leader is null || !me.Leader.Info.HasKeyword("新鱼人海盗团"))
            return Task.CompletedTask;

        AtomicOps.AddRestriction(self, RestrictionKind.CannotBeKOd, KeywordDuration.UntilNextOpponentEndPhase);
        AtomicOps.AddPowerThisTurn(self, 2000);

        if (me.LifeArea.Count > 0)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
        }

        return Task.CompletedTask;
    }
}
