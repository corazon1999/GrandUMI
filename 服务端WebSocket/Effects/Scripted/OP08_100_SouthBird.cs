using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-100 向南鸟（角色）
/// 【登场时】确认我方卡组最上方的 7 张卡牌，将其中最多 1 张"神之岛"登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - "神之岛"为卡牌名（角色卡），用 c.Info.Name 匹配。
///   - 引擎无"从卡组直接登场"的 AtomicOps，故先将选定牌移入手牌再 PlayFromHandFree 登场，
///     等价于从卡组登场该角色。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（对实战影响极小）。
///   - 客户端通过 prompt 的 extra.choiceCards 显示卡组牌的卡面（卡组牌默认不下发身份）。
/// </summary>
public class OP08_100_SouthBird : IScriptedEffect
{
    public string CardNumber => "OP08-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(7, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Name.Contains("神之岛")).ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 7 张，将其中最多 1 张\"神之岛\"登场",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                // 从卡组移入手牌再登场（等价于从卡组登场该角色）
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
