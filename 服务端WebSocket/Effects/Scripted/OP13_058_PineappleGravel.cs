using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-058 凤梨砾（事件，水 / 原白胡子海盗团）
/// 【主要】可以将我方的 1 张咚!! 转为休息状态：将对方最多 1 张力量不高于 3000 的角色
///         放回其持有者的卡组最下方。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 1 张活跃咚转为休息状态（无活跃咚则无法发动）。
///     支付后，选对方最多 1 张「当前力量 ≤ 3000」的角色放回卡组最下方（min=0 可跳过）。
///     注："力量不高于 3000" 取当前力量（含修正与附着咚），与文本一致。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_058_PineappleGravel : IScriptedEffect
{
    public string CardNumber => "OP13-058";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 成本：将我方 1 张活跃咚转为休息状态（不足则无法发动）
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-058【主要】：将我方 1 张咚!! 转为休息状态，把对方最多 1 张力量≤3000 的角色放回卡组最下方？");
        if (!use) return;

        // 支付成本
        activeDon.State = DonState.Rest;

        // 候选：对方当前力量 ≤ 3000 的角色
        bool oppTurn = s.CurrentTurnPlayer == (1 - ctx.OwnerIndex);
        var candidates = opp.Characters
            .Where(c => c.CurrentPower(opp.AttachedDonCount(c.Id), oppTurn) <= 3000)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张力量≤3000 的角色，放回其持有者卡组最下方",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ReturnFieldToDeckBottom(s, 1 - ctx.OwnerIndex, target);
    }
}
