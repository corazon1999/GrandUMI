using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-099 蒙奇·D·路飞（角色）
/// 【攻击时】咚!!-10（可以将我方场上指定数量的咚!!放回咚!!卡组）：将此角色转为活跃状态。
///
/// 实现说明：
///   - 发动成本为「咚!!-10」（将我方咚!!卡组以外的 10 张咚!! 放回咚!!卡组）。
///     触发节(DSL triggers)不支持 cost，故用脚本：先 ConfirmOptional 询问，
///     成本支付（场上咚!! ≥10 才可）后将自身转为活跃状态。
/// </summary>
public class P_099_Luffy : IScriptedEffect
{
    public string CardNumber => "P-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本需要场上合计 10 张咚!!（活跃 + 休息 + 附着）
        if (me.CostArea.Count < 10) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【攻击时】：将我方 10 张咚!! 放回咚!!卡组，使此角色转为活跃状态？");
        if (!use) return;

        // 成本：咚!!-10
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 10)) return;

        // 效果：将此角色转为活跃状态
        AtomicOps.ActivateCard(ctx.Source);
    }
}
