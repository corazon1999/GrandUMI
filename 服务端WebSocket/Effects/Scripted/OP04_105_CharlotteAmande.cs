using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-105 夏洛特·阿曼朵（角色 / 光 / 大妈海盗团）
/// 【启动主要】【每回合1次】可以丢弃我方手牌中 1 张拥有【触发】效果的卡牌：
///   将对方最多 1 张费用不高于 2 的角色转为休息状态。
///
/// 实现说明：
///   - 每回合 1 次：TurnOnceUsed 记账。
///   - 成本=丢弃手牌中 1 张"拥有【触发】效果"的卡牌（Info.Trigger 非空即视为拥有触发效果）。
///   - 收益=将对方最多 1 张费用≤2 的角色转为休息状态（RestCard）。
/// </summary>
public class OP04_105_CharlotteAmande : IScriptedEffect
{
    public string CardNumber => "OP04-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var triggerCards = me.Hand.Where(c => !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (triggerCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·阿曼朵【启动主要】：丢弃手牌中 1 张拥有【触发】的卡牌，将对方最多 1 张费用≤2 的角色转为休息状态？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = triggerCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃手牌中 1 张拥有【触发】效果的卡牌",
            triggerCards.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (disc.Count == 0) return; // 成本未支付

        me.TurnOnceUsed.Add(key);
        var cost = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, cost);

        // 收益：将对方最多 1 张费用≤2 的角色转为休息状态
        var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤2 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
