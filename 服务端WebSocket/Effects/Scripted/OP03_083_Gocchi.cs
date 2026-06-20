using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-083 哥奇（角色 / 地 / 世界政府）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，将最多 2 张卡牌放置到废弃区。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 确认卡组顶 5 张（不足取全部）；让玩家自选最多 2 张放入废弃区。
///   - 剩余卡牌按原相对顺序放回卡组最下方（自选顺序简化为原序，对实战影响极小）。
///   - 卡组顶为非公开区，prompt 传 choiceCards 以展示卡面。
/// </summary>
public class OP03_083_Gocchi : IScriptedEffect
{
    public string CardNumber => "OP03-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TopToTrash",
            "确认卡组顶 5 张，将最多 2 张放置到废弃区",
            top.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);

        foreach (var id in chosen)
        {
            var card = top.First(c => c.Id.ToString() == id);
            me.Deck.Remove(card);
            me.Trash.Add(card);
        }

        // 剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
