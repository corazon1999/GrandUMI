using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-023 嘉雅（角色 / 水 / 东海）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - "确认顶 5 张并自选顺序放顶/底"用 AtomicOps.ReorderTopK 实现。
///   - 让玩家先选择整体放"顶"或"底"，再（逐张选择形成的顺序）按所选相对顺序放回；
///     简化为保持玩家选择顺序，对实战影响极小。
///   - 经 extra.choiceCards 把这 5 张牌的卡面展示给玩家（卡组牌默认不下发身份）。
/// </summary>
public class EB03_023_Jaya : IScriptedEffect
{
    public string CardNumber => "EB03-023";

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

        // 让玩家自选这 k 张的顺序（按点选先后形成相对顺序）
        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "JayaReorder",
            "确认卡组顶 5 张，自选顺序排列后放置到卡组最上方或最下方",
            top.Select(c => c.Id.ToString()).ToList(), k, k, extra);

        // 选择整体放"最上方"或"最下方"
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将这些卡牌放置到卡组的位置", new[] { "最上方", "最下方" });

        // 按玩家选择的顺序映射回 Guid
        var order = ordered
            .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
        // 兜底：若返回不全，补齐剩余原序
        foreach (var c in top)
            if (!order.Contains(c.Id)) order.Add(c.Id);

        AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
    }
}
