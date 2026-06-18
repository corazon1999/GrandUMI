using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-047 堂吉诃德·多弗拉门戈（角色 / 水 / 3 费 / 0 / 因佩尔地狱・堂吉诃德海盗团）
/// 【启动主要】可以将此角色转为休息状态：对方持有 8 张或更多手牌的场合，
///   对方将其 2 张手牌自选顺序放回其卡组最下方。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将此角色转为休息状态"（restSelf），用 AtomicOps.RestCard 实现。
///   - 仅当对方手牌 ≥8 时该效果才有收益，故仅在该前提下询问是否发动。
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

        // 条件：对方手牌 ≥ 8 时才有收益
        if (opp.Hand.Count < 8) return;

        // 询问是否发动（"可以"=可选）
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【启动主要】：将此角色转为休息状态，令对方将 2 张手牌放回卡组最下方？");
        if (!use) return;

        // 成本：将此角色转为休息状态
        AtomicOps.RestCard(self);

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
