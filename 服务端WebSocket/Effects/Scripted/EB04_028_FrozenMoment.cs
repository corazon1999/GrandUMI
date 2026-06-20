using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-028 冰冻时刻（事件 / 水 / 海军 / cost5）
/// 【主要】可以丢弃我方的1张手牌：我方领袖拥有《海军》特征的场合，直到下个对方的结束阶段结束时为止，
///   对方最多2张力量不高于10000的角色无法攻击。
/// 【触发】将最多1张费用不高于5的角色放回其持有者的手牌。（触发节由 DSL/wf 定义处理，本脚本只实现主要。）
///
/// 实现说明：
///   - 可选成本（"可以…：…"）：先 ConfirmOptional；我方手牌<1 张无法支付。
///   - "无法攻击直到下个对方结束阶段"用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)。
///   - "力量不高于10000"按当前力量 CurrentPowerOf 判定。仅当领袖含《海军》才结算。
/// </summary>
public class EB04_028_FrozenMoment : IScriptedEffect
{
    public string CardNumber => "EB04-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Hand.Count < 1) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "冰冻时刻【主要】：丢弃我方 1 张手牌，使对方最多 2 张力量≤10000 的角色无法攻击（至下个对方结束阶段）？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var dExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, dExtra);
        if (disc.Count >= 1)
        {
            var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == disc[0]);
            if (c is not null) AtomicOps.DiscardHand(me, c);
        }
        else if (me.Hand.Count > 0) AtomicOps.DiscardHand(me, me.Hand[0]);

        // 条件：领袖含《海军》
        if (!me.Leader.Info.HasKeyword("海军")) return;

        // 效果：对方最多 2 张力量≤10000 的角色无法攻击
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 10000).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 2 张力量≤10000 的角色，使其无法攻击",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var id in chosen)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == id);
            if (tgt is not null)
                AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
