using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-119 山智（角色）
/// 【登场时】公开我方卡组最上方的 1 张卡牌，并将最多 1 张"山智"以外的费用不高于 9 的角色登场。
///   之后，将剩余的卡牌放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 公开卡组顶 1 张：若该卡为费用≤9 且名称不含"山智"的角色，则可选择将其登场。
///   - 引擎无"从卡组直接登场"的 AtomicOp；故先将该牌从卡组取出加入手牌，再用 PlayFromHandFree
///     登场（结果一致：该角色进入场上并触发其【登场时】）。
///   - 若不登场，则按文本将该公开牌放回卡组最下方（顶 1 张取出后放底）。
/// </summary>
public class OP06_119_Sanji : IScriptedEffect
{
    public string CardNumber => "OP06-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Deck.Count == 0) return;
        var topCard = me.Deck[0];

        // 公开顶 1 张
        var reveal = new List<CardInstance> { topCard };
        bool played = false;

        if (topCard.Info.Kind == CardKind.Character &&
            topCard.Info.Cost <= 9 &&
            !topCard.Info.Name.Contains("山智"))
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = reveal.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "SanjiPlayTop",
                "公开卡组顶 1 张，将其作为费用≤9 的角色登场（最多 1 张）",
                new List<string> { topCard.Id.ToString() }, 0, 1, extra);
            if (chosen.Count > 0)
            {
                me.Deck.Remove(topCard);
                me.Hand.Add(topCard);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, topCard);
                played = true;
            }
        }

        // 之后：将剩余（未登场）的公开牌放回卡组最下方
        if (!played && me.Deck.Contains(topCard))
        {
            me.Deck.Remove(topCard);
            me.Deck.Add(topCard);
        }
    }
}
