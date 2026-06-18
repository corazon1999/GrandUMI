using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-101 卡尔加拉（角色）
/// 【登场时】可以丢弃我方的 1 张手牌：确认我方卡组最上方的 5 张卡牌，公开其中合计最多 2 张
///   "蒙布朗·诺兰度"或拥有《山迪亚战士》特征的卡牌并加入手牌。之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现：可选成本（"可以…：…"），手牌为空则无法支付。ConfirmOptional 确认后丢 1 张手牌（下发
///   choiceCards 显示卡面）；探顶 5 张，可公开子集 = 名为"蒙布朗·诺兰度"或含《山迪亚战士》特征的牌，
///   选 ≤2 入手（choiceCards 给全 5 张让玩家看全身份，validChoices 仅候选），余按原序放回卡组底。
///   探顶逻辑复刻 OP13_016_Gomp / OP16_092_Robin 的检索卡规范。
/// </summary>
public class OP15_101_Calgara : IScriptedEffect
{
    public string CardNumber => "OP15-101";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return; // 无手牌可丢 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡尔加拉【登场时】：丢弃手牌 1 张，确认卡组顶 5 张并公开 ≤2 张「蒙布朗·诺兰度」或《山迪亚战士》加入手牌？");
        if (!use) return;

        // 成本：丢弃我方手牌 1 张
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (dch.Count < 1) return;
        var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 探顶 5 张，可公开子集 = 名"蒙布朗·诺兰度" 或 含《山迪亚战士》特征
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();
        var candidates = top.Where(c => c.Info.NameIs("蒙布朗·诺兰度") || c.Info.HasKeyword("山迪亚战士")).ToList();

        if (candidates.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开最多 2 张「蒙布朗·诺兰度」或《山迪亚战士》并加入手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);
            foreach (var cid in chosen)
            {
                var picked = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
                if (picked is not null) { me.Deck.Remove(picked); me.Hand.Add(picked); }
            }
        }

        // 剩余仍在顶部的卡按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
