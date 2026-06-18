using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-034 小八（角色）
/// 【启动主要】可以将此角色转为休息状态：我方领袖拥有《鱼人族》或《人鱼族》特征的场合，
///   直到下个对方的回合结束时为止，对方最多 1 张费用不高于 3 的角色无法转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本"将此角色转为休息状态"：用 RestCard 横置自身。
///   - 条件：我方领袖拥有《鱼人族》或《人鱼族》特征。
///   - 收益："对方最多 1 张费用 ≤3 的角色无法转为休息状态"用 RestrictionKind.CannotBeRested，
///     时长 UntilNextOpponentEndPhase（直到下个对方的回合结束时）。
/// </summary>
public class OP11_034_Hatchan : IScriptedEffect
{
    public string CardNumber => "OP11-034";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前置：此角色须为活跃状态才可转为休息
        if (self.IsTapped) return;

        // 条件：我方领袖拥有《鱼人族》或《人鱼族》特征
        if (!(me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族"))) return;

        // 候选：对方费用 ≤3 的角色
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3)
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小八【启动主要】：将此角色转为休息状态，使对方最多 1 张费用≤3 的角色直到下个对方回合结束无法转为休息状态？");
        if (!use) return;

        // 支付成本：横置自身
        AtomicOps.RestCard(self);

        // 收益：选择对方最多 1 张费用≤3 的角色，使其无法转为休息状态
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤3 的角色，使其无法转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
