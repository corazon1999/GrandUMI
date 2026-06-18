using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-087 妮古·罗宾（草帽一伙）
/// 原文：
///   我方领袖为"克尔拉"或"蒙奇·D·路飞"的场合，此角色获得【阻挡者】效果，费用+3。
///   【登场时】可以丢弃我方的1张手牌：对方手牌为5张或更多的场合，对方丢弃其2张手牌。
///
/// 本脚本仅实现【登场时】部分：
///   - 这是带可选成本的【登场时】：先询问是否发动；
///   - 成本：丢弃我方 1 张手牌（玩家自选，需手牌 ≥1）；
///   - 效果：若对方手牌 ≥5，对方自选丢弃 2 张。
///
/// 简化/未实现（见 summary，归引擎缺口）：
///   "我方领袖为克尔拉/路飞时获得【阻挡者】并费用+3" 是持续性静态能力，
///   引擎暂无"条件持续静态能力（持续授予关键字 / 持续改费）"通道，故此脚本不处理该静态部分。
/// </summary>
public class OP12_087_Robin : IScriptedEffect
{
    public string CardNumber => "OP12-087";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 成本需要：我方手牌 ≥1 才能丢弃
        if (me.Hand.Count < 1) return;

        // 可选效果：询问是否发动（"可以…"）
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "妮古·罗宾【登场时】：丢弃我方 1 张手牌，若对方手牌≥5 则对方丢弃 2 张？");
        if (!use) return;

        // 支付成本：丢弃我方 1 张手牌（玩家自选）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discarded = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Robin087Cost",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (discarded.Count == 0)
        {
            // 未完成丢弃 → 成本未支付，不发动
            return;
        }
        var costCard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discarded[0]);
        if (costCard is null) return;
        AtomicOps.DiscardHand(me, costCard);

        // 效果：对方手牌为 5 张或更多的场合，对方丢弃其 2 张手牌
        if (opp.Hand.Count >= 5 && ctx.Engine is not null)
        {
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, oppIdx, 2);
        }
    }
}
