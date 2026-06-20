using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-011 阿龙（角色 / 风 / 鱼人族·东海·阿龙一伙）
/// 【登场时】我方领袖拥有《鱼人族》或《东海》特征的场合，赋予我方 1 张领袖最多 1 张休息状态的咚!!。
///   之后，直到下个对方的回合结束时为止，对方最多 1 张费用不高于 5 的角色无法转为休息状态。
///
/// 实现说明：
///   - 领袖条件「含《鱼人族》或《东海》」用 || 表达。
///   - 赋予领袖 1 张休息咚用 AttachDonFromCost(... DonState.Rest)。
///   - 「无法转为休息状态（直到下个对方回合结束）」用 AddRestriction(CannotBeRested, UntilNextOpponentEndPhase)，
///     候选为对方费用≤5角色，玩家选 0..1 张。
/// </summary>
public class EB02_011_Arlong : IScriptedEffect
{
    public string CardNumber => "EB02-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 领袖条件
        if (!(me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("东海"))) return;

        // 赋予我方领袖最多 1 张休息状态的咚!!
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // 之后：对方最多 1 张费用≤5 角色，直到下个对方回合结束无法转为休息状态
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 5)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤5的角色，使其无法转为休息状态（直到下个对方回合结束）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
