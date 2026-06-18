using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-047 特拉法尔加·罗（角色）
/// 【启动主要】可以将此角色放回其持有者的手牌：对方手牌为6张或更多的场合，
///   对方将其1张手牌放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 成本"将此角色放回其持有者(我方)的手牌"用 BounceToHand 实现。
///   - 效果仅在对方手牌≥6 时有收益，且由对方自行选择 1 张手牌放回卡组最下方
///     （prompt 面向对方玩家，候选为对方手牌，传 choiceCards 供其确认）。
/// </summary>
public class OP07_047_Trafalgar_Law : IScriptedEffect
{
    public string CardNumber => "OP07-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "特拉法尔加·罗【启动主要】：将此角色放回手牌？（对方手牌≥6 张时，对方将 1 张手牌放回卡组最下方）");
        if (!use) return;

        // 成本：将此角色放回我方手牌
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 效果：对方手牌≥6 时，对方自选 1 张手牌放回卡组最下方
        if (opp.Hand.Count < 6) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(oppIdx, "OwnHand",
            "将你的 1 张手牌放回卡组最下方",
            opp.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count == 0) return;

        var picked = opp.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ReturnHandToDeckBottom(opp, picked);
    }
}
