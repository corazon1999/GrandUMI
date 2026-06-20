using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-113 莉莉丝（光 / 艾格赫德·科学家 / 1 费 2000）
///
/// 完整文本：
///   【触发】发动此卡牌的【登场时】效果。
///   【登场时】确认我方卡组最上方的 4 张卡牌，公开其中最多 1 张“莉莉丝”以外拥有【触发】效果的卡牌
///            并加入手牌。之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明（检索卡规范）：
///   - 顶部 4 张全部经 extra.choiceCards 下发，客户端全部展示供玩家确认；
///     validChoices 仅含可公开的卡（“莉莉丝”以外且 Info.Trigger 非空）。
///   - "拥有【触发】效果"判定为 CardInfo.Trigger 字段非空。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（顺序对实战影响极小）。
///   - 生命牌【触发】发动【登场时】，由 HandlesTrigger 同时响应 OnLifeRevealTrigger。
/// </summary>
public class OP13_113_Lilith : IScriptedEffect
{
    public string CardNumber => "OP13-113";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 1. 取卡组顶最多 4 张
        int peek = Math.Min(4, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 2. 可公开：“莉莉丝”以外且拥有【触发】效果（Info.Trigger 非空）的卡
        var revealable = top
            .Where(c => !c.MatchesName("莉莉丝") && !string.IsNullOrEmpty(c.Info.Trigger))
            .ToList();
        var extra = new Dictionary<string, object?>
        {
            // 检索卡规范：choiceCards = 确认到的全部牌，validChoices = 仅可公开的卡
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LilithReveal",
            $"确认卡组顶 {peek} 张，公开最多 1 张“莉莉丝”以外拥有【触发】效果的卡牌加入手牌",
            revealable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = revealable.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }

        // 3. 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
