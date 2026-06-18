using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-074 帕迪（暗 / 东海）
/// 【登场时】可以丢弃我方手牌中的 1 张事件：我方领袖为“山智”的场合，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 说明 / 简化点：
///   - 这是可选效果（"可以"）：成本为丢弃手牌中的 1 张事件，需手牌中存在事件方可发动。
///   - 丢弃事件经 extra.choiceCards 下发卡面给客户端。
///   - 收益部分仅在我方领袖为"山智"时生效：从咚卡组追加 1 张活跃咚。
///     即便领袖不是"山智"，玩家选择发动后仍需支付丢弃成本（忠实文本：成本与领袖条件相互独立，
///     条件只约束收益，不约束成本）。
/// </summary>
public class OP12_074_Pudding : IScriptedEffect
{
    public string CardNumber => "OP12-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：手牌中需存在事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count == 0) return;

        // 可选：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "帕迪【登场时】：丢弃手牌中的 1 张事件（领袖为“山智”时，从咚卡组追加 1 张活跃咚）？");
        if (!use) return;

        // 支付成本：选择并丢弃 1 张事件
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PudingDiscardEvent",
            "丢弃手牌中的 1 张事件",
            events.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        var card = chosen.Count > 0
            ? events.FirstOrDefault(c => c.Id.ToString() == chosen[0])
            : events[0];   // 超时默认弃第一张事件
        if (card is null) return;
        AtomicOps.DiscardHand(me, card);

        // 收益：领袖为"山智"时，从咚卡组追加最多 1 张活跃咚
        if (me.Leader.MatchesName("山智"))
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        }
    }
}
