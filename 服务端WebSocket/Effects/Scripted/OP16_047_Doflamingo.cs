using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-047 堂吉诃德·多弗拉门戈（角色 / 水 / 3 费 / 0 / 因佩尔地狱・堂吉诃德海盗团）
/// 【启动主要】可以将此角色转为休息状态：对方持有 8 张或更多手牌的场合，
///   对方将其 2 张手牌自选顺序放回其卡组最下方。
///
/// 实现说明：
///   - 冒号前"将此角色转为休息状态"=激活成本（restSelf），无条件先支付（用 AtomicOps.RestCard）；
///     冒号后才是效果（含"对方手牌≥8"条件）。故顺序为：发动→横置自己→再判对方手牌数决定收益，
///     即使对方手牌<8 也要横置（成本已付，仅无后续）。
///   - "对方将其 2 张手牌"由对方玩家(1-owner)自行选择(min=max=2)；对方手牌为非公开区，
///     经 extra.choiceCards 下发卡面供对方确认。
///   - "自选顺序放回卡组最下方"按对方所选顺序逐张 ReturnHandToDeckBottom（先选先放底）。
/// </summary>
public class OP16_047_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP16-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本"将此角色转为休息状态"：已横置则无法支付，不发动
        if (self.IsTapped) return;

        // "可以"=可选：是否发动（与对方手牌数无关，成本可无条件支付）
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【启动主要】：将此角色转为休息状态？（对方手牌≥8张时，令其将2张手牌放回卡组最下方）");
        if (!use) return;

        // 成本（冒号前）：将此角色转为休息状态 —— 无条件先付，按卡面顺序先于收益
        AtomicOps.RestCard(self);

        // 效果条件（冒号后）：对方手牌 ≥8 才有后续收益；不足则成本已付、无后续
        if (opp.Hand.Count < 8) return;

        // 效果：对方自选 2 张手牌放回其卡组最下方
        var oppHand = opp.Hand.ToList();
        int pick = Math.Min(2, oppHand.Count);
        if (pick == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = oppHand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OppHandToDeckBottom",
            "将你的 2 张手牌自选顺序放回卡组最下方",
            oppHand.Select(c => c.Id.ToString()).ToList(), pick, pick, extra);

        foreach (var cid in chosen)
        {
            var card = opp.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.ReturnHandToDeckBottom(opp, card);
        }
    }
}
