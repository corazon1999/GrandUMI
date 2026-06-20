using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-088 沙漠宝刀（事件 / 水 / 王下七武海·巴洛克工作室）
/// 【反击】本次战斗中，我方最多1张领袖或角色力量+2000。
///   之后，确认我方卡组最上方的3张卡牌，将其自选顺序排列并放置到卡组的最上方或最下方。
/// 【触发】抽取2张卡牌，丢弃我方的1张手牌。
///
/// 实现说明：
///   - 【反击】= EventCounter；力量+2000 用 AddPowerThisBattle；顶3张自选顺序放顶/底用 ReorderTopK。
///   - 【触发】(OnLifeRevealTrigger)：抽2张，再由玩家自选弃1张手牌。
/// </summary>
public class OP01_088_DesertSpada : IScriptedEffect
{
    public string CardNumber => "OP01-088";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 抽2张，丢弃1张手牌
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
            if (me.Hand.Count > 0)
            {
                var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "丢弃我方的1张手牌",
                    me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (discard.Count > 0)
                {
                    var card = me.Hand.First(c => c.Id.ToString() == discard[0]);
                    AtomicOps.DiscardHand(me, card);
                }
            }
            return;
        }

        // 【反击】本次战斗中，我方最多1张领袖或角色 +2000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多1张领袖或角色力量+2000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }

        // 之后：确认顶3张并自选顺序放顶/底
        int k = Math.Min(3, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DeckReorder",
            "确认卡组顶3张，自选顺序排列后放置到卡组最上方或最下方",
            top.Select(c => c.Id.ToString()).ToList(), k, k, extra);
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将这些卡牌放置到卡组的位置", new[] { "最上方", "最下方" });
        var order = ordered
            .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
        foreach (var c in top)
            if (!order.Contains(c.Id)) order.Add(c.Id);
        AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
    }
}
