using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>完整展示查看的顶牌，即使没有合法登场目标也保留确认与余牌排序。</summary>
internal static class DeckTopCharacterPlay
{
    public static async Task Resolve(EffectContext ctx, int count, int maximum,
        Func<CardInstance, bool> matches, string text, bool restState = false)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(count).ToList();
        if (top.Count == 0) return;
        var candidates = top.Where(matches).ToList();
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal", text,
            candidates.Select(card => card.Id.ToString()).ToList(), 0, Math.Min(maximum, candidates.Count),
            new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (!me.Deck.Take(top.Count).Select(card => card.Id).SequenceEqual(top.Select(card => card.Id))) return;
        foreach (var id in answer.Distinct().Take(maximum))
        {
            var card = candidates.FirstOrDefault(candidate => candidate.Id.ToString() == id);
            if (card is not null && me.Deck.Contains(card) && matches(card))
                await AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, card, restState: restState);
        }

        var rest = top.Where(card => me.Deck.Contains(card)).ToList();
        var ordered = rest;
        if (rest.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                rest.Select(card => card.Id.ToString()).ToList(), 0, rest.Count,
                new Dictionary<string, object?>
                {
                    ["allowDefaultOrder"] = true,
                    ["choiceCards"] = rest.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                });
            ordered = order.Select(id => rest.FirstOrDefault(card => card.Id.ToString() == id))
                .OfType<CardInstance>().DistinctBy(card => card.Id).ToList();
            ordered.AddRange(rest.Where(card => !ordered.Contains(card)));
        }
        if (rest.Any(card => !me.Deck.Contains(card))) return;
        foreach (var card in rest) me.Deck.Remove(card);
        me.Deck.AddRange(ordered);
    }
}
