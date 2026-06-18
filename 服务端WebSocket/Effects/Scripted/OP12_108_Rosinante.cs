using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-108 堂吉诃德·罗西南德
/// 【登场时】确认我方卡组最上方的 5 张卡牌，公开其中最多 1 张"特拉法尔加·罗"并加入手牌。
/// 之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 简化点："自选顺序放回卡组最下方" 实现为保持原相对顺序放底（顺序对实战影响极小）。
/// 客户端通过 prompt 的 extra.choiceCards 显示卡组牌的卡面。
/// </summary>
public class OP12_108_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP12-108";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 1. 取卡组顶最多 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 2. 其中名为"特拉法尔加·罗"的可公开并加入手牌（最多 1 张）
        var targets = top.Where(c => c.MatchesName("特拉法尔加·罗")).ToList();
        if (targets.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RosinanteReveal",
                "确认卡组顶 5 张，公开最多 1 张\"特拉法尔加·罗\"加入手牌",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = targets.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 3. 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
