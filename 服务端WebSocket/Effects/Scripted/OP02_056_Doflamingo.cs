using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-056 堂吉诃德·多弗拉门戈（角色）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，将其自选顺序排列并放置到卡组的最上方或最下方。
/// 【咚!!×1】【攻击时】可以丢弃我方的 1 张手牌：将对方最多 1 张费用不高于 1 的角色放回其持有者的卡组最下方。
///
/// 简化点：
/// - 顶 3 张"自选顺序放顶/底"用 ReorderTopK；让玩家选放顶或放底（顺序保持原相对顺序，对实战影响极小）。
/// - 【攻击时】需 1 个或更多被赋予的咚!!（【咚!!×1】），用 AttachedDonCount 判定。
/// </summary>
public class OP02_056_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP02-056";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            int peek = Math.Min(3, me.Deck.Count);
            if (peek == 0) return;
            var top = me.Deck.Take(peek).ToList();

            bool toBottom = (await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "确认卡组顶 3 张：放置到卡组最上方还是最下方？",
                new[] { "放到最上方", "放到最下方" })) == 1;

            AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom);
            return;
        }

        // 【咚!!×1】【攻击时】
        if (me.AttachedDonCount(self.Id) < 1) return;
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【攻击时】：丢弃 1 张手牌，将对方 1 张费用≤1 的角色放回卡组最下方？");
        if (!use) return;

        // 成本：丢弃 1 张手牌
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (disc.Count < 1) return;
        var dcard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 效果：将对方最多 1 张费用≤1 的角色放回其持有者的卡组最下方
        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤1 的角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
