using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-046 泽法(海军)
/// 【登场时】丢弃我方的 2 张手牌。（强制；手牌不足则丢全部）
/// 【启动主要】可以将此角色放置到废弃区：将最多 1 张费用不高于 5 的角色放回其持有者的手牌。
///
/// 说明：
///   - 登场时为强制弃 2 张；玩家自选弃哪 2 张（客户端经 extra.choiceCards 显示卡面）。
///   - 启动主要：先支付"自身去废弃区"，再让玩家从双方场上费用不高于 5 的角色中选最多 1 张回手。
///     "最多 1 张" 为可选（min=0）。
/// </summary>
public class OP12_046_Zephyr : IScriptedEffect
{
    public string CardNumber => "OP12-046";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
            await ResolveEnter(ctx);
        else if (ctx.Trigger == EffectTrigger.ActivatedMain)
            await ResolveActivated(ctx);
    }

    // 【登场时】强制丢弃 2 张手牌
    private static async Task ResolveEnter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int actual = Math.Min(2, me.Hand.Count);
        for (int i = 0; i < actual; i++)
        {
            if (me.Hand.Count == 0) break;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ZephyrDiscard",
                $"丢弃手牌（{i + 1}/{actual}）",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            var toDiscard = chosen.Count > 0
                ? me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0])
                : me.Hand[0];
            if (toDiscard is not null) AtomicOps.DiscardHand(me, toDiscard);
        }
    }

    // 【启动主要】将此角色放置到废弃区：将最多 1 张费用不高于 5 的角色放回其持有者的手牌
    private static async Task ResolveActivated(EffectContext ctx)
    {
        var s = ctx.State;

        // 支付：自身放置到废弃区（自送废弃，不触发 KO；AtomicOps.KO 走同步 KOCard，不触发"被 KO 时"效果）
        AtomicOps.KO(s, ctx.OwnerIndex, ctx.Source);

        // 候选：双方场上费用不高于 5 的角色
        var candidates = new List<(CardInstance card, int owner)>();
        for (int i = 0; i < 2; i++)
        {
            foreach (var c in s.Players[i].Characters)
                if (c.Info.Cost <= 5) candidates.Add((c, i));
        }
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ZephyrBounce",
            "将最多 1 张费用不高于 5 的角色放回其持有者的手牌",
            candidates.Select(c => c.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var pick = candidates.First(c => c.card.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(s, pick.owner, pick.card);
    }
}
