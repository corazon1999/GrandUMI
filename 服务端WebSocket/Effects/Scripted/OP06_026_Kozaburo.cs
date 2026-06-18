using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-026 耕四郎（角色）
/// 【登场时】将我方最多 1 张费用不高于 4 且拥有属性（斩）的角色转为活跃状态。
///   之后，我方在本回合中，无法攻击领袖。
///
/// 实现说明 / 简化点：
///   - "转为活跃状态" = ActivateCard（解除休息）。候选取我方费用≤4 且属性含"斩"的角色。
///   - "之后，我方本回合无法攻击领袖" 为持续型攻击对象限制：引擎的 RestrictionKind 仅有
///     CannotAttack / CannotBeKOd / CannotBeBlocker / CannotBeChosen，没有"不可攻击领袖"
///     这一限制类型，也无法监听本回合内后续宣告的攻击对象，故此负面限制无对应通道，
///     此处仅实现"转活跃"收益部分（参考规范：触发节无法表达的部分按惯例只实现收益）。
/// </summary>
public class OP06_026_Kozaburo : IScriptedEffect
{
    public string CardNumber => "OP06-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters
            .Where(c => c.Info.Cost <= 4 && c.Info.Property.Contains("斩") && c.IsTapped)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张费用≤4 且属性（斩）的角色转为活跃状态（选 0 张跳过）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ActivateCard(target);
    }
}
