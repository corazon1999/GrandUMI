using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-103 波特夹斯·D·艾斯（角色 / 水 / 白胡子海盗团，cost4 power6000）
/// 【登场时】抽取 2 张卡牌，将我方的 2 张手牌自选顺序排列并放置到卡组最上方或最下方。
///   之后，赋予我方领袖最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 抽 2 → 选 2 张手牌 → 用 ChooseOption 决定整体放卡组顶或底（自选顺序简化为选择时的列表顺序）。
///   - 放顶时按所选先后插入卡组最上方；放底时追加到卡组最下方。
///   - 之后赋予我方领袖最多 1 张休息状态的咚!!（可选）。
/// </summary>
public class P_103_Ace : IScriptedEffect
{
    public string CardNumber => "P-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 选 2 张手牌放回卡组（顶或底）
        if (me.Hand.Count > 0)
        {
            int needPick = Math.Min(2, me.Hand.Count);
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "选择 2 张手牌放置到卡组最上方或最下方（按选择顺序排列）",
                me.Hand.Select(c => c.Id.ToString()).ToList(), needPick, needPick);
            if (chosen.Count > 0)
            {
                int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "将所选卡牌放置到卡组的？", new[] { "卡组最上方", "卡组最下方" });

                // 按选择顺序取出
                var picked = chosen
                    .Select(id => me.Hand.First(c => c.Id.ToString() == id))
                    .ToList();
                foreach (var c in picked) me.Hand.Remove(c);

                if (where == 0)
                {
                    // 放顶：第一张选择的位于最上方
                    for (int i = picked.Count - 1; i >= 0; i--)
                        me.Deck.Insert(0, picked[i]);
                }
                else
                {
                    me.Deck.AddRange(picked);
                }
            }
        }

        // 之后：赋予我方领袖最多 1 张休息状态的咚!!
        if (me.CostArea.Any(d => d.State == DonState.Active))
        {
            bool give = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "赋予我方领袖 1 张休息状态的咚!!？");
            if (give)
                AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
        }
    }
}
