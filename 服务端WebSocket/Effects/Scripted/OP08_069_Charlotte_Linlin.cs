using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-069 夏洛特·玲玲（角色）
/// 【登场时】可以咚!!-1，并丢弃我方的1张手牌：
///   将我方卡组最上方的最多1张卡牌加入生命区最上方。
///   之后，将对方最多1张费用不高于6的角色正面朝上加入其（对方）生命区的最上方或最下方。
///
/// 实现说明 / 简化点：
/// - "可以咚!!-1 并丢弃1张手牌"为发动成本：先 ConfirmOptional 询问，确认后才支付。
///   需要费用区至少 1 张可放回的咚 且手牌至少 1 张可弃；否则不发动。
/// - "正面朝上加入其生命区"通过 MoveCharToLife 把对方角色放入对方生命区实现
///   （引擎不区分生命牌正反面，功能等价）；最上/最下由玩家选择。
/// </summary>
public class OP08_069_Charlotte_Linlin : IScriptedEffect
{
    public string CardNumber => "OP08-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本可用性：需要 1 张可放回的咚 + 至少 1 张手牌可弃
        bool canPayDon = me.CostArea.Count >= 1;
        bool canDiscard = me.Hand.Count(c => c.Id != ctx.Source.Id) >= 1;
        if (!canPayDon || !canDiscard) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "玲玲【登场时】：咚!!-1 并丢弃 1 张手牌，将卡组顶 1 张加入生命区，并把对方 1 张费用≤6角色加入其生命区？");
        if (!use) return;

        // 成本 1：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 成本 2：丢弃我方 1 张手牌
        var discardCands = me.Hand.Where(c => c.Id != ctx.Source.Id).ToList();
        var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌（成本）", discardCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (dch.Count > 0)
        {
            var d = discardCands.First(c => c.Id.ToString() == dch[0]);
            AtomicOps.DiscardHand(me, d);
        }

        // 效果 1：卡组最上方的最多 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 效果 2：将对方最多 1 张费用≤6 的角色加入其生命区（最上/最下方）
        var tgts = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 6).ToList();
        if (tgts.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = tgts.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤6 的角色，加入其生命区",
            tgts.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (ch.Count == 0) return;

        var tgt = tgts.First(c => c.Id.ToString() == ch[0]);
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色加入对方生命区的位置", new[] { "最上方", "最下方" });
        AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: where == 0);
    }
}
