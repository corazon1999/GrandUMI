using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-029 堂吉诃德·多弗拉门戈（角色）
/// 【对方的攻击时】【每回合1次】①（可以将费用区中指定数量的咚!!转为休息状态）：
///   将对方最多1张费用不高于6的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 触发节带有"将咚!!转为休息状态"的可选成本。触发节无法表达可选成本（惯例：只实现收益），
///     故此处不真正横置费用区咚，仅实现"将对方角色转为休息状态"的收益部分；
///     用 ConfirmOptional 保留"可以"的可选语义。
///   - "费用不高于6"按原本费用 Info.Cost 判定。
/// </summary>
public class OP05_029_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP05-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【对方的攻击时】：将对方最多1张费用不高于6的角色转为休息状态？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多1张费用不高于6的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
    }
}
