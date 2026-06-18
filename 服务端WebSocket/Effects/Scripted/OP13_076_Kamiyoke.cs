using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-076 神避（事件，暗）
/// 【主要】可以将我方的 5 张咚!! 转为休息状态：我方场上存在被赋予中的咚!!的场合，
///         本回合中，对方最多 1 张角色力量 -8000。
/// 【反击】可以丢弃我方的 1 张手牌：本次战斗中，我方最多 1 张领袖或角色力量 +3000。
///
/// 实现说明 / 简化点：
///   - 【主要】可选成本 = 将我方 5 张活跃咚转为休息状态（活跃咚不足 5 张则无法支付）。
///     结果条件 = 费用区存在被赋予中(Attached)的咚!!；满足时让对方最多 1 张角色本回合力量 -8000。
///   - 【反击】可选成本 = 丢弃我方 1 张手牌（无手牌则无法支付）。
///     效果 = 我方最多 1 张领袖或角色本次战斗力量 +3000。
/// </summary>
public class OP13_076_Kamiyoke : IScriptedEffect
{
    public string CardNumber => "OP13-076";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 成本：丢弃我方 1 张手牌（无手牌则无法发动）
            if (me.Hand.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "OP13-076【反击】：丢弃我方 1 张手牌，使我方最多 1 张领袖或角色本次战斗力量 +3000？");
            if (!use) return;

            // 选择丢弃的手牌
            var handExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var discardSel = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, handExtra);
            if (discardSel.Count == 0) return;
            var toDiscard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardSel[0]);
            if (toDiscard is null) return;
            AtomicOps.DiscardHand(me, toDiscard);

            // 效果：我方最多 1 张领袖或角色本次战斗 +3000
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择我方最多 1 张领袖或角色，本次战斗力量 +3000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count == 0) return;
            var buffTarget = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(buffTarget, 3000);
            return;
        }

        // ── 【主要】 ──
        // 成本：将我方 5 张活跃咚转为休息状态（活跃咚不足 5 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 5) return;

        bool useMain = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-076【主要】：将我方 5 张咚!! 转为休息状态，使对方最多 1 张角色本回合力量 -8000？");
        if (!useMain) return;

        // 支付成本：5 张活跃咚转休息
        for (int i = 0; i < 5; i++) activeDons[i].State = DonState.Rest;

        // 结果条件：场上存在被赋予中的咚!!
        bool hasAttachedDon = me.CostArea.Any(d => d.State == DonState.Attached);
        if (!hasAttachedDon) return;

        var candidates = opp.Characters.ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -8000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, -8000);
    }
}
