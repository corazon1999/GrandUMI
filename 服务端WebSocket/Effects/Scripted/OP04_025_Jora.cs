using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-025 乔拉（角色）
/// 【对方攻击时】② （可以将费用区中指定数量的咚‼转为休息状态）：将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 触发节自带"将 2 张咚转为休息"的激活成本。脚本用 ConfirmOptional 询问，发动时先把我方
///     2 张活跃咚转为休息（成本），再将对方 1 张费用≤4 的角色转为休息状态（效果）。
///   - 我方活跃咚不足 2 张时无法支付成本，不发动。
/// </summary>
public class OP04_025_Jora : IScriptedEffect
{
    public string CardNumber => "OP04-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var myActive = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (myActive.Count < 2) return;

        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 4 && !c.IsTapped)
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乔拉【对方攻击时】：将我方 2 张咚转为休息，将对方 1 张费用≤4 的角色转为休息状态？");
        if (!use) return;

        // 成本：将我方 2 张活跃咚转为休息
        myActive[0].State = DonState.Rest;
        myActive[1].State = DonState.Rest;

        // 效果：将对方最多 1 张费用≤4 的角色转为休息状态
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
