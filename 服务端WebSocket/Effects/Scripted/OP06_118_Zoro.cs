using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-118 罗罗诺亚·佐罗（角色 / 风 / 9费 / 9000 / 草帽一伙）
/// 【攻击时】【每回合1次】（可以将费用区中指定数量的咚!!转为休息状态）：将此角色转为活跃状态。
/// 【启动主要】【每回合1次】（可以将费用区中指定数量的咚‼转为休息状态）：将此角色转为活跃状态。
///
/// 说明 / 简化点：
///   - 两个能力分别有各自的【每回合1次】，用独立 key 记录。
///   - 括号内"将费用区中指定数量的咚转为休息状态"为可选成本；按惯例实现收益（将自身转为活跃），
///     并支付最小成本（横置 1 张活跃咚）。数量本身对结果无影响，故固定为 1。
///   - 两个能力效果相同（重置自身横置状态），共用同一段逻辑。
/// </summary>
public class OP06_118_Zoro : IScriptedEffect
{
    public string CardNumber => "OP06-118";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 各能力独立的每回合1次
        var key = self.Info.Number + (ctx.Trigger == EffectTrigger.OnAttackDeclare ? "-atk" : "-main") + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本咚数：①【攻击时】= 1 张，②【启动主要】= 2 张（卡面圈码①②即数量）
        int needDon = ctx.Trigger == EffectTrigger.ActivatedMain ? 2 : 1;
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).Take(needDon).ToList();
        if (activeDons.Count < needDon) return;

        // 仅当自身处于休息状态时重置才有意义
        if (!self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"佐罗：将费用区 {needDon} 张咚!!转为休息状态，将此角色转为活跃状态？");
        if (!use) return;

        // 支付成本：横置 needDon 张活跃咚
        foreach (var d in activeDons) d.State = DonState.Rest;
        // 效果：将此角色转为活跃状态
        AtomicOps.ActivateCard(self);

        me.TurnOnceUsed.Add(key);
    }
}
