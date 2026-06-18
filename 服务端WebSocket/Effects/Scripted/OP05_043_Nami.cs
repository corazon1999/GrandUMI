using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-043 润媞（角色 / 水 4 费 5000）
/// 【登场时】我方领袖为多种颜色的场合，确认我方卡组最上方的 3 张卡牌，
///   将其中最多 1 张卡牌加入手牌。之后，将剩余的卡牌自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - "多种颜色" = 我方领袖 ColorList 长度 ≥ 2。
///   - 看顶 3 张：玩家可选最多 1 张加入手牌（候选为全部 3 张，非公开区故传 choiceCards）。
///   - 剩余卡牌"自选顺序放回顶/底"：先让玩家选放顶还是放底（ChooseOption），
///     再用 AtomicOps.ReorderTopK 以剩余原相对顺序放回（顺序简化为原序，对实战影响极小）。
/// </summary>
public class OP05_043_Nami : IScriptedEffect
{
    public string CardNumber => "OP05-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅当我方领袖为多种颜色时发动
        if (me.Leader.Info.ColorList.Length < 2) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 最多 1 张加入手牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认卡组顶 3 张，将最多 1 张加入手牌",
            top.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = top.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }

        // 剩余卡牌放回卡组顶或底
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        if (rest.Count == 0) return;

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将剩余卡牌放置到卡组最上方还是最下方？", new[] { "最上方", "最下方" });
        AtomicOps.ReorderTopK(me, rest.Select(c => c.Id).ToList(), toBottom: where == 1);
    }
}
