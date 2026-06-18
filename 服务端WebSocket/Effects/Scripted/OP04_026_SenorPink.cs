using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-026 赛诺尔·平克（角色 / 风）
/// 【攻击时】①（可以将费用区中指定数量的咚‼转为休息状态）：我方领袖拥有《堂吉诃德海盗团》特征的场合，
///   将对方最多1张费用不高于4的角色转为休息状态。之后，当本回合结束时，将我方最多1张咚!!转为活跃状态。
///
/// 实现：OnAttackDeclare，仅本卡为攻击者时；成本①将1张活跃咚转休息；收益休息对方≤4费角色；
/// 之后登记回合结束任务 RefreshOwnDon（回合末活跃1张休息咚）。
/// </summary>
public class OP04_026_SenorPink : IScriptedEffect
{
    public string CardNumber => "OP04-026";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.State.CurrentBattle?.AttackerCardId != self.Id) return;                 // 仅本卡攻击时
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;                        // 条件
        if (!me.CostArea.Any(d => d.State == DonState.Active)) return;                   // 需有活跃咚付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "赛诺尔·平克：将1张活跃咚转休息，休息对方≤4费角色，本回合结束时活跃1咚？");
        if (!use) return;

        // 成本①：将1张活跃咚转为休息
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;
        don.State = DonState.Rest;

        // 收益：将对方最多1张费用≤4角色转为休息
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 4).ToList();
        if (cands.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤4的角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var t = cands.FirstOrDefault(c => c.Id.ToString() == ch[0]);
                if (t is not null) AtomicOps.RestCard(t);
            }
        }

        // 之后：回合结束时将我方最多1张休息咚转为活跃
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask { Kind = "RefreshOwnDon", Owner = ctx.OwnerIndex });
    }
}
