using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-047 战国
/// 【登场时】可以丢弃我方的 1 张手牌：确认我方卡组最上方的 5 张卡牌，
/// 公开其中最多 2 张"战国"以外的拥有《海军》特征的卡牌并加入手牌。
/// 之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 说明：
///   - 丢弃 1 张手牌是发动该效果的代价（"可以"=可选，手牌为空则无法发动）。
///   - 公开对象："战国"以外 且 拥有《海军》特征 的卡，最多 2 张加入手牌。
/// 简化点："自选顺序放回卡组最下方" 实现为保持原相对顺序放底（对实战影响极小）。
/// 客户端通过 prompt 的 extra.choiceCards 显示卡组/手牌牌的卡面。
/// </summary>
public class OP12_047_Sengoku : IScriptedEffect
{
    public string CardNumber => "OP12-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // "可以丢弃 1 张手牌"：手牌为空则无法发动；询问是否发动
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "战国：丢弃 1 张手牌，确认卡组顶 5 张并公开最多 2 张《海军》加入手牌？");
        if (!use) return;

        // 支付代价：丢弃 1 张手牌（玩家自选）
        var dextra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "SengokuDiscard",
            "丢弃 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, dextra);
        var toDiscard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand[0];
        if (toDiscard is not null) AtomicOps.DiscardHand(me, toDiscard);

        // 确认卡组顶最多 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 公开对象："战国"以外 且 拥有《海军》特征 的卡，最多 2 张
        var navy = top
            .Where(c => c.Info.HasKeyword("海军") && !c.MatchesName("战国"))
            .ToList();
        if (navy.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "SengokuReveal",
                "公开最多 2 张\"战国\"以外的《海军》卡加入手牌",
                navy.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);
            foreach (var cid in chosen)
            {
                var picked = navy.FirstOrDefault(c => c.Id.ToString() == cid);
                if (picked is not null && me.Deck.Contains(picked))
                {
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                }
            }
        }

        // 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
