using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-117 燃烧剑（事件 / 空岛・山迪亚战士）
/// 【主要】可以将我方生命区最上方的 1 张卡牌放置到废弃区：
///   将对方最多 1 张费用不高于 7 的角色 KO。
/// 【触发】可以将我方生命区最上方的 1 张卡牌加入手牌：
///   将我方最多 1 张手牌加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - DSL 的 cost 节仅有 lifeToHand（生命入手），无"生命入废弃"成本键，故用脚本实现。
///   - 成本为"将生命区最上方 1 张放置到废弃区"，整体为"可以"=可选，故先 ConfirmOptional。
///   - 无可 KO 目标（对方无费用≤7 角色）时仍允许玩家发动支付成本，但通常不会；
///     若选择不发动则直接返回，不支付成本。
///   - "费用不高于 7"取场上角色当前费用。
///   - 【触发】先可选将剩余生命顶加入手牌，再从手牌中选择最多 1 张放回生命顶。
/// </summary>
public class OP08_117_BurningSword : IScriptedEffect
{
    public string CardNumber => "OP08-117";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (me.LifeArea.Count == 0) return;

            bool useTrigger = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "燃烧剑【触发】：是否将我方生命区最上方的 1 张卡牌加入手牌？");
            if (!useTrigger) return;

            var triggerLifeTop = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            triggerLifeTop.IsLifeFaceUp = false;
            me.Hand.Add(triggerLifeTop);

            var handChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "将我方最多 1 张手牌加入生命区最上方",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (handChoice.Count > 0)
            {
                var toLife = me.Hand.First(c => c.Id.ToString() == handChoice[0]);
                AtomicOps.HandToLife(me, toLife, toTop: true, faceUp: false);
            }
            return;
        }

        // 成本：将生命区最上方 1 张放置到废弃区，无生命则无法支付
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "燃烧剑【主要】：将生命区最上方 1 张放置到废弃区，KO 对方 1 张费用≤7 的角色？");
        if (!use) return;

        // 支付成本
        var lifeTop = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Trash.Add(lifeTop);

        // 收益：KO 对方最多 1 张费用≤7 的角色
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 7).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "KO 对方 1 张费用≤7 的角色（最多 1 张）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
