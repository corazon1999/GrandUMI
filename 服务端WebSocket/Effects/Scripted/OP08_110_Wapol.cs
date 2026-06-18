using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-110 瓦帕（角色，光）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，公开其中最多 1 张"神之岛"并加入手牌。
///           之后，将剩余的卡牌自选顺序放回卡组最下方，
///           并将我方手牌中最多 1 张"神之岛"登场。
///
/// 实现说明 / 简化点：
///   - "神之岛"为舞台卡，按名称 MatchesName("神之岛") 匹配。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（对实战影响极小）。
///   - PlayFromHandFree 同时支持舞台卡登场（替换已有舞台），故可登场手牌中的"神之岛"。
///   - 卡组牌默认不下发身份，故 prompt 通过 extra.choiceCards 显示卡面。
/// </summary>
public class OP08_110_Wapol : IScriptedEffect
{
    public string CardNumber => "OP08-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 确认卡组最上方 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();

            // 公开其中最多 1 张"神之岛"加入手牌
            var islands = top.Where(c => c.MatchesName("神之岛")).ToList();
            if (islands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                    "确认卡组顶 5 张，公开最多 1 张\"神之岛\"加入手牌",
                    islands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (chosen.Count > 0)
                {
                    var picked = islands.First(c => c.Id.ToString() == chosen[0]);
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                }
            }

            // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) me.Deck.Remove(c);
            me.Deck.AddRange(rest);
        }

        // 并将我方手牌中最多 1 张"神之岛"登场
        var playable = me.Hand.Where(c => c.MatchesName("神之岛")).ToList();
        if (playable.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "将手牌中最多 1 张\"神之岛\"登场",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = playable.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, p);
            }
        }
    }
}
