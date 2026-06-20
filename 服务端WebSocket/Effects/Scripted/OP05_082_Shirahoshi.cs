using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-082 白星（角色）
/// 【启动主要】可以将此角色转为休息状态，并将我方废弃区中的 2 张卡牌自选顺序放回卡组最下方：
///   对方持有 6 张或更多手牌的场合，对方丢弃其 1 张手牌。
///
/// 实现说明 / 简化点：
///   - 成本为"将此角色转为休息状态 + 将废弃区 2 张放回卡组底"（"可以"=可选发动）。
///     废弃区不足 2 张则无法支付成本，不发动。
///   - "自选顺序"由玩家选择 2 张废弃区卡牌（顺序即放回顺序），按选择顺序放回卡组底。
///   - 收益：对方手牌 ≥6 时弃 1 张（OpponentDiscardChosen，需 ctx.Engine 非空）。
/// </summary>
public class OP05_082_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "OP05-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Trash.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "白星【启动主要】：横置此角色并将废弃区 2 张放回卡组底，若对方手牌≥6 则对方弃 1 张？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 成本：选择废弃区 2 张，按选择顺序放回卡组最下方
        var trashExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashToDeckBottom",
            "选择废弃区 2 张，按选择顺序放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 2, 2, trashExtra);
        foreach (var id in picks)
        {
            var c = me.Trash.FirstOrDefault(x => x.Id.ToString() == id);
            if (c != null) AtomicOps.ReturnTrashToDeckBottom(me, c);
        }

        // 收益：对方手牌 ≥6 时丢弃 1 张
        if (opp.Hand.Count >= 6 && ctx.Engine != null)
        {
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
        }
    }
}
