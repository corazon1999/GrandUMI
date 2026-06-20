using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-021 维奥拉（角色）
/// 【对方攻击时】② （可以将费用区中指定数量的咚‼转为休息状态）：将对方最多 1 张咚!! 转为休息状态。
///
/// 实现说明 / 简化点：
///   - 触发节自带"将 2 张咚转为休息"的激活成本。脚本用 ConfirmOptional 询问是否发动，
///     发动时先把我方 2 张活跃咚转为休息（成本），再将对方 1 张活跃咚转为休息（效果）。
///   - 我方活跃咚不足 2 张时无法支付成本，不发动。
/// </summary>
public class OP04_021_Viola : IScriptedEffect
{
    public string CardNumber => "OP04-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 需要 2 张活跃咚支付成本
        var myActive = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (myActive.Count < 2) return;

        // 对方须有可被横置的活跃咚
        if (!opp.CostArea.Any(d => d.State == DonState.Active)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "维奥拉【对方攻击时】：将我方 2 张咚转为休息，将对方 1 张咚转为休息状态？");
        if (!use) return;

        // 成本：将我方 2 张活跃咚转为休息
        myActive[0].State = DonState.Rest;
        myActive[1].State = DonState.Rest;

        // 效果：将对方最多 1 张活跃咚转为休息状态
        var oppActive = opp.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (oppActive != null) oppActive.State = DonState.Rest;
    }
}
