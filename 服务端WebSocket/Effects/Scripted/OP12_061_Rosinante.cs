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
/// 实现说明 / 简化点：
///   - 用 ContinuousEffect.CostDelta 对我方手牌中名为"特拉法尔加·罗"、原本费用≥4 的卡注册
///     本回合(仅我方回合) -2 的费用修正，影响其打出费用。Predicate 限定为我方回合且该卡仍在手牌。
///   - 原文"下次…一次"为单次消耗，引擎无"消耗一次后失效"的持续通道，故近似为"本回合内该类卡
///     登场费用均 -2"（略宽于原文，实战影响有限）。回合结束随 turnPlayer 变化自动失效，并在登场时
///     先去重防叠加。
///   - 能力 1 未实现：PreKO 仅对"将被 KO 的卡自身"派发，无法让罗西南德监听另一张"特拉法尔加·罗"
///     的将被 KO 事件（规范 12.6：持续监听他卡被 KO，引擎无通道）。
/// </summary>
public class OP12_061_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP12-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        var key = "OP12-061-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        // 成本：咚!!-1
        if (me.ActiveDonCount < 1) return Task.CompletedTask;
        AtomicOps.ReturnDonToDeck(me, 1);

        me.TurnOnceUsed.Add(key);

        // 注册本回合费用修正：手牌中费用≥4 的"特拉法尔加·罗"打出费用 -2
        var selfId = self.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.Name.Contains("特拉法尔加·罗") && c.Info.Cost >= 4,
            },
            CostDelta = -2,
            Predicate = (s, sideIdx, c) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].Hand.Any(h => h.Id == c.Id),
        });

        return Task.CompletedTask;
    }
}
