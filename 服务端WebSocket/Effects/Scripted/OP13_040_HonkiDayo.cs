using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-040 我知道你很强……所以一开始我就动真格的了!!（事件，风）
/// 【主要】可以将我方的 2 张咚!! 转为休息状态：对方最多 2 张处于休息状态且费用不高于 7 的角色，
///         在下个对方的重置阶段中不会转为活跃状态。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 2 张活跃咚转为休息状态（需有 ≥2 张活跃咚才能支付）。
///     效果 = 让对方最多 2 张「休息中且当前费用≤7」的角色获得 CannotActivateNextReset 标记。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_040_HonkiDayo : IScriptedEffect, IEventMainAvailability
{
    public string CardNumber => "OP13-040";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public string? GetEventMainUnavailableReason(
        GameState state,
        int ownerIndex,
        CardInstance source,
        int effectivePlayCost)
    {
        var me = state.Players[ownerIndex];
        if (me.ActiveDonCount < effectivePlayCost + 2)
            return $"发动 OP13-040 除出牌费用外还需要 2 张活跃咚!!（当前共需 {effectivePlayCost + 2} 张）";
        return HasLegalTarget(state, ownerIndex)
            ? null
            : "对方没有处于休息状态且当前费用不高于 7 的角色，无法发动 OP13-040";
    }

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // CardPlayer 已先支付实际出牌费用；此处再次校验额外成本与目标，防住绕过动作入口的调用。
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 2) return;

        var candidates = LegalTargets(ctx.State, ctx.OwnerIndex);
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-040【主要】：将我方 2 张咚!! 转为休息状态，使对方最多 2 张休息中且费用≤7 的角色，在下个对方重置阶段中不会转为活跃？");
        if (!use) return;

        // 支付成本：2 张活跃咚转休息
        for (int i = 0; i < 2; i++) activeDons[i].State = DonState.Rest;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
            "选择最多 2 张休息中且费用≤7 的对方角色，使其在下个对方重置阶段不会转为活跃",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 2);

        foreach (var cid in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.PreventActivateNextReset(card);
        }
    }

    private static bool HasLegalTarget(GameState state, int ownerIndex)
        => LegalTargets(state, ownerIndex).Count > 0;

    private static List<CardInstance> LegalTargets(GameState state, int ownerIndex)
        => state.Players[1 - ownerIndex].Characters
            .Where(card => card.IsTapped && state.CurrentCostOf(card) <= 7)
            .ToList();
}
