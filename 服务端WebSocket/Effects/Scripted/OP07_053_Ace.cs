using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-053 波特卡斯·D·艾斯（角色 / 水 5 费 6000，白胡子海盗团）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【登场时】抽取 2 张卡牌，并将我方的 2 张手牌自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 【阻挡者】为纯关键词，引擎处理，本脚本只实现【登场时】。
///   - 抽 2 后，从手牌选 2 张（若手牌不足 2 张则按可选数量），逐张让玩家选择放卡组顶或底。
///   - 「自选顺序」简化：玩家逐张选择目标位置（顶/底），按选择先后形成相对顺序。
/// </summary>
public class OP07_053_Ace : IScriptedEffect
{
    public string CardNumber => "OP07-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 选 2 张手牌放回卡组顶/底
        int need = Math.Min(2, me.Hand.Count);
        if (need == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择 2 张手牌放置到卡组最上方或最下方",
            me.Hand.Select(c => c.Id.ToString()).ToList(), need, need);

        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is null) continue;
            int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将该手牌放置到卡组的位置", new[] { "最上方", "最下方" });
            me.Hand.Remove(card);
            if (pos == 0) me.Deck.Insert(0, card);
            else me.Deck.Add(card);
        }
    }
}
