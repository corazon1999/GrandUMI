using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-038 舞蹈石（事件）
/// 【反击】本次战斗中，我方最多1张领袖或角色力量+4000。之后，可以丢弃我方的1张手牌。
///   那样做的场合，将我方的最多3张咚!!转为活跃状态。
/// 【触发】将对方最多1张领袖或费用不高于3的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 反击：先选1张我方领袖或角色 +4000（本次战斗）；之后用 ConfirmOptional 询问是否丢1张手牌，
///     丢弃后将费用区最多3张休息状态的咚!!转为活跃状态（直接修改 DonCard.State）。
///   - 触发节："费用不高于3"按原本费用 Info.Cost；将对方领袖或符合的角色转为休息状态。
/// </summary>
public class OP05_038_DanceDanceDance : IScriptedEffect
{
    public string CardNumber => "OP05-038";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】将对方最多1张领袖或费用不高于3的角色转为休息状态
            var trigCands = new List<CardInstance> { opp.Leader };
            trigCands.AddRange(opp.Characters.Where(c => c.Info.Cost <= 3));

            var trigChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
                "将对方最多1张领袖或费用不高于3的角色转为休息状态",
                trigCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (trigChosen.Count > 0)
            {
                var tgt = trigCands.First(c => c.Id.ToString() == trigChosen[0]);
                AtomicOps.RestCard(tgt);
            }
            return;
        }

        // 【反击】本次战斗中，我方最多1张领袖或角色力量+4000
        var buffCands = new List<CardInstance> { me.Leader };
        buffCands.AddRange(me.Characters);

        var buffChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多1张领袖或角色力量+4000",
            buffCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (buffChosen.Count > 0)
        {
            var tgt = buffCands.First(c => c.Id.ToString() == buffChosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 4000);
        }

        // 之后：可以丢弃我方的1张手牌
        if (me.Hand.Count == 0) return;
        bool discard = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "是否丢弃我方1张手牌，将我方最多3张咚!!转为活跃状态？");
        if (!discard) return;

        var discardChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardChosen.Count == 0) return;

        var toDiscard = me.Hand.First(c => c.Id.ToString() == discardChosen[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 将我方最多3张休息状态的咚!!转为活跃状态
        int activated = 0;
        foreach (var d in me.CostArea)
        {
            if (activated >= 3) break;
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                activated++;
            }
        }
    }
}
