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
///     效果 = 让对方最多 2 张「休息中且原本费用≤7」的角色获得 CannotActivateNextReset 标记。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_040_HonkiDayo : IScriptedEffect
{
    public string CardNumber => "OP13-040";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 成本：将我方 2 张活跃咚转为休息状态（不足 2 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-040【主要】：将我方 2 张咚!! 转为休息状态，使对方最多 2 张休息中且费用≤7 的角色，在下个对方重置阶段中不会转为活跃？");
        if (!use) return;

        // 支付成本：2 张活跃咚转休息
        for (int i = 0; i < 2; i++) activeDons[i].State = DonState.Rest;

        // 候选：对方处于休息状态且原本费用不高于 7 的角色
        var candidates = opp.Characters
            .Where(c => c.IsTapped && c.Info.Cost <= 7)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
            "选择最多 2 张休息中且费用≤7 的对方角色，使其在下个对方重置阶段不会转为活跃",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 2);

        foreach (var cid in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.PreventActivateNextReset(card);
        }
    }
}
