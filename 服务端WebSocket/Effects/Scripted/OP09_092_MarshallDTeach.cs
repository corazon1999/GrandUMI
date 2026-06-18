using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-092 马歇尔·D·提奇（角色 / 地 3 费 4000，白胡子海盗团）
///
/// 完整文本：
///   【启动主要】可以将此角色转为休息状态：我方手牌张数比对方手牌张数少 3 张或更多的场合，
///   抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现：
///   - 成本"可以将此角色转为休息状态"为可选：先 ConfirmOptional 询问，且自身需为活跃状态才能横置。
///   - 条件"我方手牌比对方少 3 张或更多"= me.Hand.Count <= opp.Hand.Count - 3，直接在脚本中比较。
///   - 满足条件则抽 2 张，再让玩家选 1 张手牌丢弃。
///
/// 简化点：无（手牌差比较 DSL 无对应条件键，故用脚本直接计算）。
/// </summary>
public class OP09_092_MarshallDTeach : IScriptedEffect
{
    public string CardNumber => "OP09-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 自身已是休息状态则无法支付成本
        if (self.IsTapped) return;

        // 可选成本：将此角色转为休息状态
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "提奇【启动主要】：将此角色转为休息状态？（手牌比对方少 3 张或更多时抽 2 弃 1）");
        if (!use) return;

        // 支付成本：横置自身
        AtomicOps.RestCard(self);

        // 条件：我方手牌张数比对方少 3 张或更多
        if (me.Hand.Count > opp.Hand.Count - 3) return;

        // 抽 2 张
        AtomicOps.Draw(s, ctx.OwnerIndex, 2);

        // 丢弃我方 1 张手牌（强制）
        if (me.Hand.Count == 0) return;
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (chosen.Count == 0) return;
        var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, card);
    }
}
