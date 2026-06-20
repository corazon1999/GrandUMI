using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-026 萨奇斯（角色）
/// 【咚!!×1】【攻击时】【每回合1次】可以将我方 1 张费用为 3 或更高的角色转为休息状态：
///   将此角色转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】发动条件由引擎在触发节判定，脚本只实现成本与收益。
///   - 成本为横置我方 1 张费用≥3 的角色（可为自身以外的角色，亦可含本卡若费用≥3）。
///     用 ConfirmOptional 询问，再 ChooseCards 选成本目标并 RestCard。
///   - 收益：将此角色（攻击者自身）转为活跃 ActivateCard。
///   - 每回合 1 次用 TurnOnceUsed 标记。
/// </summary>
public class OP05_026_Saatchis : IScriptedEffect
{
    public string CardNumber => "OP05-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：我方费用≥3 且当前为活跃状态的角色
        var costCands = me.Characters.Where(c => c.Info.Cost >= 3 && !c.IsTapped).ToList();
        if (costCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨奇斯【攻击时】：横置 1 张我方费用≥3 的角色，使此角色转为活跃？");
        if (!use) return;

        var paid = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张费用≥3 的我方角色转为休息状态（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (paid.Count < 1) return;
        var costTarget = costCands.First(c => c.Id.ToString() == paid[0]);
        AtomicOps.RestCard(costTarget);

        me.TurnOnceUsed.Add(key);

        // 收益：此角色转为活跃状态
        AtomicOps.ActivateCard(ctx.Source);
    }
}
