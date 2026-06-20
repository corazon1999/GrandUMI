using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-029 我听到了……女士落泪的声音。（事件 / 水 / 班克禁区・草帽一伙 / cost1）
/// 【主要】我方领袖为"山智"的场合，确认我方卡组最上方的3张卡牌，公开其中最多1张"山智"或事件
///   并加入手牌。之后，将剩余的卡牌放置到废弃区。
/// 【反击】可以丢弃我方的1张手牌：本次战斗中，我方最多1张"山智"力量+4000。
///
/// 实现说明：
///   - 主要段：仅领袖名为"山智"才结算。看顶 3 张，候选为名含"山智"或事件类，公开 1 张加入手牌，
///     其余全部放置废弃区。
///   - 反击段：可选成本弃我方 1 张手牌；本次战斗我方最多 1 张名含"山智"的角色/领袖 +4000。
/// </summary>
public class EB04_029_SanjiCry : IScriptedEffect
{
    public string CardNumber => "EB04-029";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            if (!me.Leader.Info.NameContains("山智")) return;

            int peek = Math.Min(3, me.Deck.Count);
            if (peek == 0) return;
            var top = me.Deck.Take(peek).ToList();

            var cands = top.Where(c => c.Info.NameContains("山智") || c.Info.Kind == CardKind.Event).ToList();
            if (cands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                    "确认卡组顶 3 张，公开最多 1 张「山智」或事件加入手牌",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (chosen.Count > 0)
                {
                    var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                }
            }

            // 之后：剩余仍在顶部的卡牌放置废弃区
            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) { me.Deck.Remove(c); me.Trash.Add(c); }
            return;
        }

        // 【反击】
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            if (me.Hand.Count < 1) return; // 无法支付成本

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "落泪之声【反击】：丢弃我方 1 张手牌，使我方最多 1 张「山智」本次战斗力量+4000？");
            if (!use) return;

            // 成本：弃我方 1 张手牌
            var dExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
                "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, dExtra);
            if (disc.Count >= 1)
            {
                var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == disc[0]);
                if (c is not null) AtomicOps.DiscardHand(me, c);
            }
            else if (me.Hand.Count > 0) AtomicOps.DiscardHand(me, me.Hand[0]);

            // 效果：我方最多 1 张「山智」+4000
            var sanji = new List<CardInstance>();
            if (me.Leader.Info.NameContains("山智")) sanji.Add(me.Leader);
            sanji.AddRange(me.Characters.Where(c => c.Info.NameContains("山智")));
            if (sanji.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择我方最多 1 张「山智」，本次战斗力量+4000",
                sanji.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = sanji.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisBattle(tgt, 4000);
            }
        }
    }
}
