using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-030 托雷波尔（角色）
/// 【登场时】将对方最多 1 张处于休息状态且费用不高于 5 的角色KO。
/// 【对方攻击时】② （可以将费用区中指定数量的咚‼转为休息状态）：将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 两个能力分别用 OnEnterField 与 OnOppAttackDeclare 时机处理。
///   - 【对方攻击时】② 触发节自带"将 2 张咚转为休息"的激活成本：脚本用 ConfirmOptional 询问，
///     发动时先把我方 2 张活跃咚转为休息（成本），再横置对方 1 张费用≤4 角色（效果）。
/// </summary>
public class OP04_030_Trebol : IScriptedEffect
{
    public string CardNumber => "OP04-030";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 【登场时】KO 对方 1 张休息且费用≤5 的角色
            var koCands = opp.Characters
                .Where(c => c.IsTapped && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 5)
                .ToList();
            if (koCands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "KO 对方最多 1 张休息且费用≤5 的角色",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else // OnOppAttackDeclare
        {
            var myActive = me.CostArea.Where(d => d.State == DonState.Active).ToList();
            if (myActive.Count < 2) return;

            var restCands = opp.Characters
                .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 4 && !c.IsTapped)
                .ToList();
            if (restCands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "托雷波尔【对方攻击时】：将我方 2 张咚转为休息，将对方 1 张费用≤4 的角色转为休息状态？");
            if (!use) return;

            myActive[0].State = DonState.Rest;
            myActive[1].State = DonState.Rest;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤4 的角色转为休息状态",
                restCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = restCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
    }
}
