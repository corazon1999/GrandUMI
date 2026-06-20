using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-009 超级快跑鸭部队（角色 / 炎）
/// 【攻击时】本回合中，可以将我方1张活跃状态的领袖力量-5000：当本回合结束时，将此角色放回持有者的手牌。
///
/// 实现：OnAttackDeclare。可选成本（活跃领袖本回合-5000），同意后登记回合结束任务 ReturnSelfToHand
/// （TurnEngine.EnterEndPhase 执行，将此角色放回手牌）。
/// </summary>
public class OP04_009_SuperRunDuck : IScriptedEffect
{
    public string CardNumber => "OP04-009";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Leader.IsTapped) return;                                                 // 须有活跃领袖作成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "超级快跑鸭部队：将我方活跃领袖力量-5000，本回合结束时此角色放回手牌？");
        if (!use) return;

        AtomicOps.AddPowerThisTurn(me.Leader, -5000);
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "ReturnSelfToHand",
            SourceCardId = self.Id.ToString(),
            Owner = ctx.OwnerIndex,
        });
    }
}
