using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-023 阿龙（角色）
/// 【登场时】可以丢弃我方的 1 张手牌：直到下个对方的回合结束时为止，
///   对方最多 1 张处于休息状态的领袖无法攻击。
///
/// 实现说明 / 简化点：
///   - "可以丢弃 1 张手牌" 为可选支付：在手牌选择中选 0 张则跳过整个效果。
///   - "处于休息状态的领袖" 取对方领袖且 IsTapped。
///   - "无法攻击（直到下个对方回合结束）" 用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)，
///     与 OP12-043 库赞同款表达。
///   - 【触发】节将对方≤1张费用≤4角色横置由触发机制单独处理，此处仅实现【登场时】。
/// </summary>
public class OP06_023_Arlong : IScriptedEffect
{
    public string CardNumber => "OP06-023";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 目标前提：对方领袖处于休息状态
        if (opp.Leader is null || !opp.Leader.IsTapped) return;
        if (me.Hand.Count == 0) return;

        // 可选支付：丢弃 1 张手牌（选 0 张 = 不发动）
        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "可以丢弃 1 张手牌：使对方休息状态的领袖无法攻击（选 0 张跳过）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1, handExtra);
        if (discardPick.Count == 0) return;

        var discardCard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardPick[0]);
        if (discardCard is null) return;
        AtomicOps.DiscardHand(me, discardCard);

        AtomicOps.AddRestriction(opp.Leader, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
