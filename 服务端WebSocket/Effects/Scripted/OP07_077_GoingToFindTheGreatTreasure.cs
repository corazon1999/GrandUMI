using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-077 要去拿下『大秘宝』了！！！（事件）
/// 【主要】我方领袖拥有《百兽海盗团》或《大妈海盗团》特征的场合，确认我方卡组最上方的 5 张卡牌，
///   公开其中最多 1 张拥有《百兽海盗团》或《大妈海盗团》特征的卡牌并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
/// 【触发】发动此卡牌的【主要】效果。
///
/// 实现说明 / 简化点：
///   - 前提：我方领袖拥有《百兽海盗团》或《大妈海盗团》特征。
///   - 看顶 5 张，公开其中最多 1 张含上述任一特征的卡加入手牌，其余按原相对顺序放回卡组最下方。
///   - 卡组牌为非公开区，通过 extra.choiceCards 下发卡面供客户端展示。
///   - 【触发】等价于发动【主要】效果，故同一逻辑覆盖 EventMain 与 OnLifeRevealTrigger。
/// </summary>
public class OP07_077_GoingToFindTheGreatTreasure : IScriptedEffect
{
    public string CardNumber => "OP07-077";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Leader is null ||
            !(me.Leader.Info.HasKeyword("百兽海盗团") || me.Leader.Info.HasKeyword("大妈海盗团")))
            return;

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top
            .Where(c => c.Info.HasKeyword("百兽海盗团") || c.Info.HasKeyword("大妈海盗团"))
            .ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 5 张，公开最多 1 张《百兽海盗团》或《大妈海盗团》卡牌加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 之后：将剩余仍在顶部的卡按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
