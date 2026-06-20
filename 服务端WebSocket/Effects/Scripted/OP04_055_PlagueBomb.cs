using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-055 瘟疫弹（事件 / 水 / 百兽海盗团）
/// 【主要】可以丢弃我方手牌中的 1 张"冰鬼"，将 1 张费用不高于 4 的角色放回其持有者的卡组最下方：
///   从我方废弃区中登场 1 张"冰鬼"。
/// 【触发】发动此卡牌的【主要】效果。（触发节由引擎/DSL 处理。）
///
/// 实现说明：
///   - 成本（"可以…"=可选，两部分均为成本，须能全部支付才发动收益）：
///       1) 丢弃手牌中 1 张名为"冰鬼"的卡（DiscardHand）；
///       2) 将双方 1 张费用≤4 角色放回其持有者卡组最下方（ReturnFieldToDeckBottom）。
///   - 收益：从我方废弃区登场 1 张名为"冰鬼"的角色（PlayFromTrashFree）。
///   - 名称匹配用 MatchesName("冰鬼")。
/// </summary>
public class OP04_055_PlagueBomb : IScriptedEffect
{
    public string CardNumber => "OP04-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本前置可用性检查
        var iceHand = me.Hand.Where(c => c.MatchesName("冰鬼")).ToList();
        if (iceHand.Count == 0) return;

        var costTargets = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters.Where(c => c.CurrentCost() <= 4)) costTargets.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => c.CurrentCost() <= 4)) costTargets.Add((c, 1 - ctx.OwnerIndex));
        if (costTargets.Count == 0) return;

        var iceTrash = me.Trash.Where(c => c.MatchesName("冰鬼") && c.Info.Kind == CardKind.Character).ToList();
        if (iceTrash.Count == 0) return; // 废弃区无可登场的"冰鬼"，无收益则不发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "瘟疫弹【主要】：丢弃手牌 1 张「冰鬼」并将 1 张费用≤4 角色放回卡组底，从废弃区登场 1 张「冰鬼」?");
        if (!use) return;

        // 成本 1：丢弃手牌中 1 张"冰鬼"
        var iceExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = iceHand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pickDiscard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃手牌中的 1 张「冰鬼」（成本）",
            iceHand.Select(c => c.Id.ToString()).ToList(), 1, 1, iceExtra);
        if (pickDiscard.Count == 0) return;
        var discardCard = iceHand.First(c => c.Id.ToString() == pickDiscard[0]);
        AtomicOps.DiscardHand(me, discardCard);

        // 成本 2：将 1 张费用≤4 角色放回其持有者卡组最下方
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costTargets.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var pickReturn = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将 1 张费用≤4 的角色放回其持有者卡组最下方（成本）",
            costTargets.Select(t => t.card.Id.ToString()).ToList(), 1, 1, costExtra);
        if (pickReturn.Count > 0)
        {
            var rt = costTargets.First(t => t.card.Id.ToString() == pickReturn[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, rt.owner, rt.card);
        }

        // 收益：从我方废弃区登场 1 张"冰鬼"
        var revive = me.Trash.FirstOrDefault(c => c.MatchesName("冰鬼") && c.Info.Kind == CardKind.Character);
        if (revive is not null)
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, revive);
    }
}
