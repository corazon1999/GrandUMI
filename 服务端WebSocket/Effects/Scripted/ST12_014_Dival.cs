using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST12-014 迪巴鲁（事件，飞鱼骑士团）
/// 【阻挡者】为纯关键词，由引擎处理，不在此实现。
/// 【登场时】确认我方卡组最上方的 3 张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 用 AtomicOps.ReorderTopK 实现"看顶 K 张后按指定顺序放回顶/底"。
///   - "自选顺序"：让玩家用 ChooseCards 依次选择全部顶部卡，选择的先后即为排列顺序；
///     未选满则按原相对顺序补齐，保证全部放回。
///   - "放置到卡组最上方或最下方"：用 ChooseOption 让玩家二选一。
///   - 顶部不足 3 张时按实际张数处理。
/// </summary>
public class ST12_014_Dival : IScriptedEffect
{
    public string CardNumber => "ST12-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;

        var top = me.Deck.Take(peek).ToList();

        // 让玩家自选顺序排列这 peek 张卡（按选择先后定序）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderTop",
            "确认卡组顶 " + peek + " 张，按选择顺序排列",
            top.Select(c => c.Id.ToString()).ToList(), 0, peek, extra);

        // 构建最终顺序：先按玩家所选顺序，再补齐未选中的（保持原相对顺序）
        var order = new List<Guid>();
        foreach (var id in ordered)
        {
            var c = top.FirstOrDefault(x => x.Id.ToString() == id);
            if (c != null && !order.Contains(c.Id)) order.Add(c.Id);
        }
        foreach (var c in top)
        {
            if (!order.Contains(c.Id)) order.Add(c.Id);
        }

        // 选择放置到卡组最上方还是最下方
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将这 " + peek + " 张卡牌放置到卡组的位置",
            new[] { "卡组最上方", "卡组最下方" });

        AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
    }
}
