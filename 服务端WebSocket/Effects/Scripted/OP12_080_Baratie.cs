using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-080 巴拉蒂（舞台）
/// 【启动主要】可以将此舞台放回其持有者的卡组最下方：我方领袖为"山智"的场合，
/// 确认我方卡组最上方的 3 张卡牌，公开其中最多 1 张事件并加入手牌。
/// 之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将此舞台放回卡组最下方"。该成本不在 DSL 既有 cost 集合内（DSL 仅支持
///     donReturn/restSelf/selfToTrash/handDiscard），故用脚本实现。
///   - 仅当我方领袖名为"山智"时本效果才有收益，故仅在该前提下询问是否发动；非"山智"时不提示
///     （即便支付成本也只会白白把舞台放回卡组，玩家不会愿意），与文本结果一致。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（对实战影响极小）。
///   - 客户端通过 prompt 的 extra.choiceCards 显示卡组牌的卡面（卡组牌默认不下发身份）。
/// </summary>
public class OP12_080_Baratie : IScriptedEffect
{
    public string CardNumber => "OP12-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅当领袖为"山智"时本效果有意义
        if (!me.Leader.Info.NameContains("山智")) return;

        // 询问是否发动（"可以"=可选）
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "巴拉蒂【启动主要】：将此舞台放回卡组最下方，确认卡组顶 3 张并公开最多 1 张事件加入手牌？");
        if (!use) return;

        // 成本：将此舞台放回其持有者的卡组最下方
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 效果：确认卡组最上方 3 张，公开其中最多 1 张事件加入手牌
        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var events = top.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "BaratieReveal",
                "确认卡组顶 3 张，公开最多 1 张事件加入手牌",
                events.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = events.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
