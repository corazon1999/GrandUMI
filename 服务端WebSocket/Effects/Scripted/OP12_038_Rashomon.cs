using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-038 二刀流 抽刀 罗生门（事件）
/// 【主要】可以将我方的 2 张咚!! 转为休息状态：将对方最多 2 张处于休息状态且原本的费用不高于 4 的角色 KO。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 说明：
///   - "将我方的 2 张咚!! 转为休息状态" 是发动【主要】的可选额外成本（需 ≥2 张活跃咚）。
///     与事件本身的印刷费用（1 费，由引擎打出时支付）相互独立。
///   - KO 目标限定：对方「处于休息状态」且「原本费用 ≤4」的角色，最多 2 张。
///     原本费用取 c.Info.Cost（卡面印刷费用）。
/// </summary>
public class OP12_038_Rashomon : IScriptedEffect
{
    public string CardNumber => "OP12-038";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】本次战斗中，我方领袖力量 +3000。
            var me0 = ctx.State.Players[ctx.OwnerIndex];
            AtomicOps.AddPowerThisBattle(me0.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 可选额外成本：将我方 2 张活跃咚转为休息
        if (me.ActiveDonCount < 2) return;

        // KO 目标：对方处于休息状态且原本费用 ≤4 的角色
        var targets = opp.Characters
            .Where(c => c.IsTapped && c.Info.Cost <= 4)
            .ToList();
        if (targets.Count == 0) return; // 无可选目标则发动无意义

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗生门【主要】：将我方 2 张咚!! 转为休息状态，KO 对方最多 2 张休息且原本费用≤4 的角色？");
        if (!use) return;

        // 支付成本：2 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return;

        // 效果：KO 最多 2 张
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
            "选择最多 2 张对方休息且原本费用≤4 的角色 KO",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var cid in chosen)
        {
            var card = targets.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null)
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, card);
        }
    }
}
