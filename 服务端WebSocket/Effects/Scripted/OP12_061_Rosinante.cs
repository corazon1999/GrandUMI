using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-061 堂吉诃德·罗西南德（领航 4 费 5000，海军/堂吉诃德海盗团）
/// 1. 【每回合1次】我方的"特拉法尔加·罗"将要被 KO 的场合，可以改为将我方生命区最上方的 1 张
///    卡牌加入手牌，使该"特拉法尔加·罗"不会被 KO。（替换/防 KO）
/// 2. 【启动主要】【每回合1次】咚!!-1：本回合中，我方下次从手牌中登场的费用为 4 或更高的
///    "特拉法尔加·罗"需支付的费用减少 2。
///
/// 本脚本实现能力 2（启动主要：咚-1，本回合手牌中费用≥4 的"特拉法尔加·罗"登场费用 -2）。
///
/// 实现说明：
///   - 用 OneShotPlayDiscount 注册"本回合下一次"从手牌登场、原本费用≥4 的"特拉法尔加·罗" -2 的
///     一次性减费（CardPlayer 打出该类卡时消费一次即移除；回合末 TurnEngine 统一清空）。
///     这精确对应原文"下次…一次"（反馈#135：旧用 ContinuousEffect 会让本回合所有罗都减费、用不完）。
///   - 能力 1 未实现：PreKO 仅对"将被 KO 的卡自身"派发，无法让罗西南德监听另一张"特拉法尔加·罗"
///     的将被 KO 事件（规范 12.6：持续监听他卡被 KO，引擎无通道）。
/// </summary>
public class OP12_061_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP12-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        var key = "OP12-061-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：咚!!-1
        if (me.CostArea.Count < 1) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        me.TurnOnceUsed.Add(key);

        // 一次性减费：我方"本回合下一次"从手牌登场、原本费用≥4 的"特拉法尔加·罗" -2。
        // 打出一次即被 CardPlayer 消费、回合末 TurnEngine 清空，精确对应原文"下次…一次"。
        ctx.State.OneShotPlayDiscounts.RemoveAll(d => d.Owner == owner && d.NameContains == "特拉法尔加·罗"); // 防叠加
        ctx.State.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = owner,
            Amount = 2,
            MinCost = 4,
            NameContains = "特拉法尔加·罗",
        });
    }
}
