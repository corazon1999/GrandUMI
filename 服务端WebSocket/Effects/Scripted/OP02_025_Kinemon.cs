using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-025 锦卫门（领航 / 风）
/// 【启动主要】【每回合1次】我方场上的角色不多于1张的场合，本回合中，下次我方从手牌中登场费用为3或更多
///   且拥有《和之国》特征的角色卡牌，需支付的费用减少1。
///
/// 实现：ActivatedMain（每回合1次，角色≤1张时）登记一次性减费 OneShotPlayDiscount，本回合内首个满足
/// （费用≥3 且含《和之国》的角色）登场时被 HandPlayCost/CardPlayer 消费一次。
/// </summary>
public class OP02_025_Kinemon : IScriptedEffect
{
    public string CardNumber => "OP02-025";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Characters.Count > 1) return Task.CompletedTask;                          // 角色≤1张
        var key = "OP02-025-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;                    // 每回合1次
        me.TurnOnceUsed.Add(key);

        ctx.State.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = ctx.OwnerIndex,
            Amount = 1,
            MinCost = 3,
            Keyword = "和之国",
            Kind = "Character",
        });
        return Task.CompletedTask;
    }
}
