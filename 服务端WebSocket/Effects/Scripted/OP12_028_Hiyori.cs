using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-028 光月日和（1 费 0 力）
/// 【启动主要】可以将我方的 1 张咚!! 和此角色转为休息状态：
///   我方领袖为"罗罗诺亚·佐罗"的场合，确认我方卡组最上方的 5 张卡牌，
///   公开其中最多 1 张拥有属性（斩）的卡牌或绿色（风）事件，并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 代价（脚本自行支付，引擎不会代付）：
///   - 将我方 1 张活跃咚!! 转为休息状态
///   - 将此角色（自身）转为休息状态
/// 两项代价缺一不可：自身已休息或无活跃咚则无法发动。
///
/// 简化点：
///   - "自选顺序放回卡组最下方" 实现为保持原相对顺序放底（顺序对实战影响极小）。
///   - 该效果文本无"每回合 1 次"，故不加 oncePerTurn 限制。
/// 客户端通过 prompt 的 extra.choiceCards（{id, number} 列表）显示卡组牌卡面。
/// </summary>
public class OP12_028_Hiyori : IScriptedEffect
{
    public string CardNumber => "OP12-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖为"罗罗诺亚·佐罗"
        if (!me.Leader.MatchesName("罗罗诺亚·佐罗")) return;

        // 代价 1：需要一张活跃咚!! 可转休息
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;
        // 代价 2：自身需为活跃状态才能转休息
        if (ctx.Source.IsTapped) return;

        // 支付代价
        activeDon.State = DonState.Rest;
        activeDon.AttachedToCardId = null;
        ctx.Source.IsTapped = true;

        // 效果：确认卡组顶 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 可公开并加入手牌：拥有属性（斩）的卡牌 或 绿色（风）事件
        var candidates = top.Where(c =>
            c.Info.Property == "斩"
            || (c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("绿")))
            .ToList();

        if (candidates.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HiyoriReveal",
                "确认卡组顶 5 张，公开最多 1 张属性（斩）卡牌或绿色事件加入手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
