using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-029 堂吉诃德·多弗拉门戈（角色）
/// 【对方的攻击时】【每回合1次】①（可以将费用区中指定数量的咚!!转为休息状态）：
///   将对方最多1张费用不高于6的角色转为休息状态。
///
/// 实现说明：
///   - 玩家确认发动后，横置 1 张未被赋予的活跃咚支付成本。
///   - "费用不高于6"按当前费用判定。
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

        var candidates = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 6).ToList();
        var costDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active && d.AttachedToCardId is null);
        if (candidates.Count == 0 || costDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【对方的攻击时】：将对方最多1张费用不高于6的角色转为休息状态？");
        if (!use) return;

        costDon.State = DonState.Rest;
        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多1张费用不高于6的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
    }
}
