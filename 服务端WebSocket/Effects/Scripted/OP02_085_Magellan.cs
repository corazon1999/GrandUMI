using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-085 麦哲伦（角色）
/// 【登场时】咚!!-1（成本：将我方场上 1 张咚!!放回咚!!卡组）：对方将其场上的 1 张咚!!放回咚!!卡组。
/// 【对方的回合中】此角色被KO时：对方将其场上的 2 张咚!!放回咚!!卡组。
///
/// 实现说明 / 简化点：
///   - "对方将其场上的咚!!放回咚!!卡组" 用 AtomicOps.ReturnDonToDeck(opp, n)（作用于对方 PlayerState）。
///   - 【登场时】成本为我方咚-1（可选）：先 ConfirmOptional，确认后我方 ReturnDonToDeck(me,1)，再令对方放回 1 张。
///     若我方场上无咚可放回，则成本无法支付，不发动。
///   - 【对方的回合中】被KO时：用 OnKO 触发，判定当前为对方回合，令对方放回 2 张咚。
/// </summary>
public class OP02_085_Magellan : IScriptedEffect
{
    public string CardNumber => "OP02-085";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 【对方的回合中】此角色被KO时：对方放回 2 张咚
            if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return; // 仅对方回合中
            AtomicOps.ReturnDonToDeck(opp, 2);
            return;
        }

        // 【登场时】咚!!-1（可选成本：我方放回 1 张咚），对方放回 1 张咚
        bool meHasReturnable = me.CostArea.Count > 0;
        bool oppHasReturnable = opp.CostArea.Any(d => d.State == DonState.Active || d.State == DonState.Rest);
        if (!meHasReturnable) return;  // 无法支付成本
        if (!oppHasReturnable) return; // 无收益则不发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "麦哲伦【登场时】：将我方 1 张咚!!放回咚!!卡组，令对方将其 1 张咚!!放回咚!!卡组？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.ReturnDonToDeck(opp, 1);
    }
}
