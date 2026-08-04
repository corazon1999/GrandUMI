using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-066 阿悟
/// 【攻击时】我方场上的咚!!张数不多于6张的场合，确认我方卡组最上方的2张卡牌，
/// 将其自选顺序排列并放置到卡组的最上方或最下方。
/// </summary>
public class OP15_066_Satori : IScriptedEffect
{
    public string CardNumber => "OP15-066";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.TotalDonInCostArea > 6) return;

        int count = Math.Min(2, me.Deck.Count);
        if (count == 0) return;

        var top = me.Deck.Take(count).ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };

        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DeckReorder",
            $"阿悟：确认卡组顶{count}张，自选顺序排列",
            top.Select(c => c.Id.ToString()).ToList(), count, count, extra);

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将这些卡牌放置到卡组的位置", new[] { "最上方", "最下方" });

        var order = ordered
            .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
        foreach (var card in top)
            if (!order.Contains(card.Id)) order.Add(card.Id);

        AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
    }
}
