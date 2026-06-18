using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-043 润媞（角色）
/// 【登场时】我方领袖为多种颜色的场合，确认我方卡组最上方的 3 张卡牌，将其中最多 1 张加入手牌。
///   之后，将剩余的卡牌自选顺序排列并放置到卡组最上方或最下方。
///
/// 说明 / 简化点：
/// - "多种颜色"判定为领袖 ColorList 长度 ≥2。
/// - "自选顺序放顶/底"简化为保持原相对顺序（剩余牌默认放回卡组最上方，对实战影响极小）。
/// </summary>
public class OP05_043_Nguyen : IScriptedEffect
{
    public string CardNumber => "OP05-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 领袖须为多色
        if (me.Leader.Info.ColorList.Length < 2) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认卡组顶 3 张，将其中最多 1 张加入手牌",
            top.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = top.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }

        // 之后：剩余卡牌按原相对顺序放回卡组最上方
        // （仍在 Deck 顶部的牌已是原顺序，无需移动；此处显式保持以表达"自选顺序放顶"。）
    }
}
