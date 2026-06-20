using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST17-001 克洛克达尔（角色）
/// 【登场时】公开我方卡组最上方的 1 张卡牌；若该牌拥有《王下七武海》特征，
///   则抽 2 张，并将我方的 1 张手牌放回卡组最上方。
///
/// 实现说明：
/// - "公开卡组顶 1 张"通过 BroadcastReveal 向双方展示其卡面（不离开卡组）。
/// - 据公开牌是否含《王下七武海》特征作为后续门槛，需脚本判断（DSL 无该条件门）。
/// - "将 1 张手牌放回卡组最上方"用 me.Hand 移除后 Insert(0, ...) 实现；
///   先抽 2 张再放回（与文本"抽 2 张，将 1 张手牌放回卡组最上方"一致）。
/// </summary>
public class ST17_001_Crocodile : IScriptedEffect
{
    public string CardNumber => "ST17-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Deck.Count == 0) return;

        // 公开卡组最上方 1 张
        var topCard = me.Deck[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { topCard.Info.Number });

        // 条件：该牌拥有《王下七武海》特征
        if (!topCard.Info.HasKeyword("王下七武海")) return;

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 将我方 1 张手牌放回卡组最上方
        if (me.Hand.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将 1 张手牌放回卡组最上方",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            me.Hand.Remove(picked);
            me.Deck.Insert(0, picked);
        }
    }
}
