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
/// 持续光环（登场时注册 ContinuousEffect）：
///   "我方领袖为克尔拉/路飞时此角色获得【阻挡者】并费用+3" 由 GrantKeyword="阻挡者"+CostDelta=3
///   的条件持续效果实现，按领袖名动态评估。
/// </summary>
public class OP12_087_Robin : IScriptedEffect
{
    public string CardNumber => "OP12-087";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // ── 持续光环：我方领袖为"克尔拉"或"蒙奇·D·路飞"→ 此角色获得【阻挡者】、费用+3（登场时无条件注册，按领袖动态评估）──
        var selfId0 = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId0.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId0.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            CostDelta = 3,
            Predicate = (st, side, card) => card.Id == selfId0 &&
                (st.Players[owner].Leader.Info.Name == "克尔拉" || st.Players[owner].Leader.Info.Name == "蒙奇·D·路飞"),
        });

        var me = ctx.State.Players[owner];
        int oppIdx = 1 - owner;
        var opp = ctx.State.Players[oppIdx];

        // 成本需要：我方手牌 ≥1 才能丢弃（【登场时】可选效果）
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
