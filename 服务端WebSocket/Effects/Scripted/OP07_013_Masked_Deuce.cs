using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-013 玛斯库德·丢斯（角色）
/// 【登场时】我方领袖为"波特夹斯·D·艾斯"的场合，确认我方卡组最上方的 5 张卡牌，
///   公开其中最多 1 张"波特夹斯·D·艾斯"或红色（炎）事件并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 仅当我方领袖名称含"波特夹斯·D·艾斯"时本效果有意义，否则不发动。
///   - 候选 = 名称含"波特夹斯·D·艾斯"的牌 或 颜色含"红"的事件（红色事件）。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底。
/// </summary>
public class OP07_013_Masked_Deuce : IScriptedEffect
{
    public string CardNumber => "OP07-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.NameContains("波特夹斯·D·艾斯")) return;

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            c.Info.Name.Contains("波特夹斯·D·艾斯") ||
            (c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("红"))
        ).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 5 张，公开最多 1 张\"波特夹斯·D·艾斯\"或红色事件加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
