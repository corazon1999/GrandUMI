using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-072 泽法（领袖 / 暗・地 / FILM・新生海军，power5000）
/// 【攻击时】咚!!-4（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   将对方最多 1 张费用不高于 3 的角色 KO。之后，本回合中，此领袖力量 +1000。
///
/// 实现说明：
///   - 攻击时=OnAttackDeclare；成本"咚!!-4"=将我方 4 张咚放回咚卡组（ReturnDonToDeck），可选 → ConfirmOptional。
///   - 需我方咚卡组外（费用区）至少 4 张活跃/休息咚可放回；不足或拒绝则不发动。
///   - KO 对方费用≤3 角色用当前费用 CurrentCostOf；之后本回合自身 +1000。
/// </summary>
public class OP02_072_Zephyr : IScriptedEffect
{
    public string CardNumber => "OP02-072";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        // 成本：咚!!-4（费用区可放回的咚 ≥4）
        if (me.CostArea.Count < 4) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "泽法【攻击时】：咚!!-4（将 4 张咚放回咚卡组），KO 对方最多 1 张费用≤3 角色并本回合此领袖+1000？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 4)) return;

        var cands = ctx.State.Players[oppIdx].Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 3).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "KO 对方最多 1 张费用≤3 的角色",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, oppIdx, tgt);
            }
        }

        // 之后：本回合中此领袖 +1000
        AtomicOps.AddPowerThisTurn(ctx.Source, 1000);
    }
}
