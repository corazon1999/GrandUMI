using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-037 鬼气 九刀流 阿修罗 拔剑 亡者游戏（事件）
/// 【主要】可以将我方的 3 张咚!! 转为休息状态：将对方合计最多 2 张角色或咚!! 转为休息状态。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 说明：
///   - "将我方的 3 张咚!! 转为休息状态" 是发动【主要】的可选额外成本（需 ≥3 张活跃咚）。
///     与事件本身的印刷费用（1 费，由引擎在打出事件时支付）相互独立。
///   - 目标为"对方角色或咚!!"，合计最多 2 张：玩家可任意分配在对方角色与对方活跃咚上。
///     先选要转休息的对方角色（0..2），剩余张数再从对方活跃咚中扣（自动取活跃咚）。
///   - 咚不是 CardInstance，无法与角色放进同一 ChooseCards 候选，故拆成两步交互。
/// </summary>
public class OP12_037_Asura : IScriptedEffect
{
    public string CardNumber => "OP12-037";

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

        // 可选额外成本：将我方 3 张活跃咚转为休息
        if (me.ActiveDonCount < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿修罗【主要】：将我方 3 张咚!! 转为休息状态，使对方合计最多 2 张角色或咚!! 转为休息？");
        if (!use) return;

        // 支付成本：3 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 3) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 3) return; // 理论上不会发生

        // 效果：对方合计最多 2 张「角色或咚!!」转为休息
        int budget = 2;

        // 第一步：选择要转休息的对方角色（只在活跃角色中选，休息的转休息无意义）
        var oppActiveChars = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (oppActiveChars.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                $"选择最多 {budget} 张对方角色转为休息状态",
                oppActiveChars.Select(c => c.Id.ToString()).ToList(), 0, budget);
            foreach (var cid in chosen)
            {
                var card = oppActiveChars.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) { AtomicOps.RestCard(card); budget--; }
            }
        }

        // 第二步：剩余预算从对方活跃咚中扣，转为休息
        if (budget > 0)
        {
            int oppActiveDon = opp.CostArea.Count(d => d.State == DonState.Active);
            int restDon = Math.Min(budget, oppActiveDon);
            if (restDon > 0)
            {
                bool restThem = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    $"将对方 {restDon} 张活跃咚!! 转为休息状态？");
                if (restThem)
                {
                    int done = 0;
                    foreach (var d in opp.CostArea)
                    {
                        if (done >= restDon) break;
                        if (d.State == DonState.Active) { d.State = DonState.Rest; done++; }
                    }
                }
            }
        }
    }
}
