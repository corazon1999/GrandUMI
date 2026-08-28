using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-054 奈美（角色）
/// 【阻挡者】（关键词，由引擎处理）。
/// 【登场时】我方领袖为多种颜色的场合：抽取 3 张卡牌，
///   将我方的 2 张手牌自选顺序放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - "多种颜色"判定为领袖 ColorList.Length >= 2。
///   - "自选顺序放置到卡组顶或底"：逐张选择 1 张手牌，并选择放到卡组顶还是底，共处理 2 张。
///     两张都放牌顶时，第 1 张最终在第 2 张上方；两张都放牌底时，第 2 张最终在第 1 张下方。
///   - 抽牌后若手牌不足 2 张则按实际数量处理。
/// </summary>
public class OP11_054_Nami : IScriptedEffect
{
    public string CardNumber => "OP11-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅在领袖为多种颜色时发动
        if (me.Leader.Info.ColorList.Length < 2) return;

        // 抽取 3 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 3);

        // 将 2 张手牌自选顺序放到卡组顶/底
        int count = Math.Min(2, me.Hand.Count);
        int placedTop = 0;   // 已放到卡组顶的张数，用于保序：先选的在更上方（避免二次 Insert(0) 反转，反馈 #197）
        CardInstance? firstPlacedCard = null;
        int? firstPlacement = null;
        for (int i = 0; i < count; i++)
        {
            var hand = me.Hand.ToList();
            if (hand.Count == 0) break;

            // 手牌属"不下发身份"区域，须随选择面板下发 {id, number}，否则前端 PromptOverlay 找不到卡号、卡图显示为占位（反馈 #19）
            var handExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            string selectionPrompt = BuildSelectionPrompt(i, count, firstPlacedCard, firstPlacement);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                selectionPrompt,
                hand.Select(c => c.Id.ToString()).ToList(), 1, 1, handExtra);
            if (pick.Count == 0) break;

            var card = hand.First(c => c.Id.ToString() == pick[0]);
            var placementOptions = BuildPlacementOptions(i, count, firstPlacement);

            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                BuildPlacementPrompt(i, count, card, firstPlacedCard, firstPlacement),
                placementOptions);
            if (where is not 0 and not 1) break;

            me.Hand.Remove(card);
            if (where == 0) me.Deck.Insert(placedTop++, card);   // 放顶时按选择次序保序，先选的更靠上
            else me.Deck.Add(card);

            if (i == 0)
            {
                firstPlacedCard = card;
                firstPlacement = where;
            }
        }
    }

    private static string BuildSelectionPrompt(
        int index,
        int count,
        CardInstance? firstPlacedCard,
        int? firstPlacement)
    {
        if (index == 0)
            return $"第 1/{count} 张：选择要放回卡组的手牌；随后选择放到牌顶或牌底。";

        string firstCard = firstPlacedCard is null ? "第 1 张" : $"第 1 张（{CardLabel(firstPlacedCard)}）";
        return firstPlacement == 0
            ? $"第 2/{count} 张：选择要放回卡组的手牌。{firstCard}已放牌顶；若本张也放牌顶，本张会位于第 1 张下方。"
            : $"第 2/{count} 张：选择要放回卡组的手牌。{firstCard}已放牌底；若本张也放牌底，本张会位于第 1 张下方并成为最终最下方。";
    }

    private static string BuildPlacementPrompt(
        int index,
        int count,
        CardInstance card,
        CardInstance? firstPlacedCard,
        int? firstPlacement)
    {
        string prompt = $"第 {index + 1}/{count} 张（{CardLabel(card)}）：选择放回卡组后的最终位置。";
        if (index == 0 || firstPlacedCard is null) return prompt;

        string firstPosition = firstPlacement == 0 ? "牌顶" : "牌底";
        return $"{prompt}第 1 张（{CardLabel(firstPlacedCard)}）已放{firstPosition}。";
    }

    private static IReadOnlyList<string> BuildPlacementOptions(int index, int count, int? firstPlacement)
    {
        if (count == 1)
            return new[]
            {
                "放到牌顶：本张成为卡组最上方",
                "放到牌底：本张成为卡组最下方",
            };

        if (index == 0)
            return new[]
            {
                "放到牌顶：本张最终最上；第 2 张放牌顶时位于本张下方",
                "放到牌底：第 2 张放牌顶时本张最终最下；放牌底时第 2 张位于本张下方",
            };

        return firstPlacement == 0
            ? new[]
            {
                "放到牌顶：本张在第 1 张下方（第 1 张最上）",
                "放到牌底：本张成为卡组最下方",
            }
            : new[]
            {
                "放到牌顶：本张成为卡组最上方",
                "放到牌底：本张在第 1 张下方（本张最下）",
            };
    }

    private static string CardLabel(CardInstance card)
        => $"{card.Info.Number} {card.Info.Name}";
}
