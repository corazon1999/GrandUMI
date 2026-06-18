using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-067 阿鹤(海军)
/// 【登场时】确认我方卡组最上方的 5 张卡牌，公开其中最多 1 张拥有《海军》特征的卡牌
/// 并加入手牌，将剩余的卡牌自选顺序放回卡组最下方。之后，丢弃我方的 1 张手牌。
///
/// 实现说明（检索卡规范）：
///   - "确认 5 张"：choiceCards 下发顶部全部 5 张，客户端全部展示让玩家看全；
///     validChoices 仅含可公开的《海军》卡，其余置灰仅供确认、不可选。
///   - "自选顺序放回卡组最下方" 实现为保持原相对顺序放底（顺序对实战影响极小）。
/// 客户端通过 prompt 的 extra.choiceCards（{id, number} 列表）显示卡面（卡组/手牌默认不下发身份）。
/// </summary>
public class OP16_067_Otsuru : IScriptedEffect
{
    public string CardNumber => "OP16-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 1. 取卡组顶最多 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 2. 确认顶部全部 peek 张（全部下发供客户端展示），其中《海军》可公开并加入手牌（最多 1 张）
        var navy = top.Where(c => c.Info.HasKeyword("海军")).ToList();
        var extra = new Dictionary<string, object?>
        {
            // 检索卡规范：choiceCards = 确认到的全部牌，validChoices = 仅可公开的《海军》
            ["choiceCards"] = top
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OtsuruReveal",
            $"确认卡组顶 {peek} 张，公开最多 1 张《海军》加入手牌",
            navy.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = navy.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }

        // 3. 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);

        // 4. 丢弃 1 张手牌（强制，手牌为空则跳过）
        if (me.Hand.Count > 0)
        {
            var extra2 = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OtsuruDiscard",
                "丢弃 1 张手牌",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra2);
            var toDiscard = dchosen.Count > 0
                ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
                : me.Hand[0];   // 超时默认弃第一张
            if (toDiscard is not null) AtomicOps.DiscardHand(me, toDiscard);
        }
    }
}
