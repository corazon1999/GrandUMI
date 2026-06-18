using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-077 佩罗娜（角色 / 水 / 恐怖之船海盗团）
/// 【登场时】确认我方卡组最上方的5张卡牌，将其自选顺序排列并放置到卡组的最上方或最下方。
///
/// 实现说明：
///   - 确认顶5张并自选顺序放顶/底用 AtomicOps.ReorderTopK；玩家点选先后形成相对顺序。
/// </summary>
public class OP01_077_Perona : IScriptedEffect
{
    public string CardNumber => "OP01-077";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };

        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DeckReorder",
            "确认卡组顶5张，自选顺序排列后放置到卡组最上方或最下方",
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
