using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-065 Mr.3(加尔迪诺)（角色 / 水 / 因佩尔地狱・原巴洛克工作室，cost5 power5000）
/// 【阻挡者】（关键词，由引擎处理）
/// 【我方的回合结束时】可以丢弃我方的 1 张手牌：将此角色转为活跃状态。
///
/// 实现说明：
///   - 关键词【阻挡者】纯由引擎处理，这里只实现主动效果部分。
///   - 【我方的回合结束时】=OnMyTurnEnd；可选成本"丢弃 1 张手牌"用 ConfirmOptional 询问，
///     再 ChooseCards 选弃；支付后 ActivateCard 使自身转活跃。
///   - 自身已是活跃则无收益，仍按文本允许执行（ActivateCard 幂等）。
/// </summary>
public class OP02_065_Mr3 : IScriptedEffect
{
    public string CardNumber => "OP02-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.3【我方的回合结束时】：丢弃我方 1 张手牌，将此角色转为活跃状态？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        var disc = me.Hand.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, disc);
        AtomicOps.ActivateCard(ctx.Source);
    }
}
