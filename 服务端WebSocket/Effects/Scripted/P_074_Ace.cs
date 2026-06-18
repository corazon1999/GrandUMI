using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-074 波特卡斯·D·艾斯（角色 / 水 / 白胡子海盗团）
/// 【启动主要】可以将此角色放回其持有者的手牌：确认我方卡组最上方的 5 张卡牌，
///   自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将此角色放回持有者手牌"（BounceToHand），不在 DSL cost 集合内，故脚本实现。
///   - "确认顶 5 张 + 自选顺序放顶/底"用玩家二选一（放顶 / 放底），顺序简化为保持原相对顺序
///     （ReorderTopK 按原序）。客户端通过 prompt 的 extra.choiceCards 展示卡组牌面。
/// </summary>
public class P_074_Ace : IScriptedEffect
{
    public string CardNumber => "P-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾斯【启动主要】：将此角色放回手牌，确认卡组顶 5 张并自选放置到卡组顶或底？");
        if (!use) return;

        // 成本：将此角色放回持有者的手牌
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 效果：确认卡组最上方 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        // 让玩家选择放置位置：放卡组顶 or 放卡组底（顺序保持原相对顺序）
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "确认卡组顶 5 张，选择放置位置", new[] { "放回卡组最上方", "放回卡组最下方" });

        // 通过 prompt 展示已确认的牌（选 0 张，仅用于展示）
        _ = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认卡组顶 5 张", new List<string>(), 0, 0, extra);

        var order = top.Select(c => c.Id).ToList();
        AtomicOps.ReorderTopK(me, order, toBottom: opt == 1);
    }
}
