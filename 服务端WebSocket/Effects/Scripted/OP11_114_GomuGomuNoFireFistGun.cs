using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-114 橡皮橡皮火拳枪（事件 / 光 1 费，草帽一伙）
/// 【主要】可以将我方的 3 张咚!! 转为休息状态：双方生命卡牌合计张数为 5 张或更多的场合，
///   将对方最多 1 张原本的费用不高于 5 的角色 KO。
/// 【反击】本次战斗中，我方最多 1 张领袖力量 +3000。
///
/// 实现：
///   - 【主要】(EventMain)：可选额外成本 = 将我方 3 张活跃咚转为休息（需 ≥3 张活跃咚）。
///     发动门槛 = 双方生命合计 ≥5（me.LifeCount + opp.LifeCount）。
///     KO 目标 = 对方原本费用 ≤5（c.Info.Cost）的角色，最多 1 张。
///   - 【反击】(EventCounter)：我方最多 1 张领袖力量 +3000（本次战斗）。本卡领袖唯一，
///     直接给领袖 +3000（可选，玩家可选 0/1 张）。
/// </summary>
public class OP11_114_GomuGomuNoFireFistGun : IScriptedEffect
{
    public string CardNumber => "OP11-114";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】我方最多 1 张领袖力量 +3000（本次战斗）
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
                "本次战斗中，我方最多 1 张领袖力量 +3000",
                new List<string> { me.Leader.Id.ToString() }, 0, 1);
            if (chosen.Count > 0)
                AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 发动门槛：双方生命合计 ≥5
        if (me.LifeCount + opp.LifeCount < 5) return;

        // 可选额外成本：将我方 3 张活跃咚转为休息
        if (me.ActiveDonCount < 3) return;

        // KO 目标：对方原本费用 ≤5 的角色
        var targets = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "橡皮橡皮火拳枪【主要】：将我方 3 张咚!! 转为休息状态，KO 对方最多 1 张原本费用≤5 的角色？");
        if (!use) return;

        // 支付成本：3 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 3) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 3) return;

        // 效果：KO 最多 1 张
        var chosenKo = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用≤5 的角色 KO",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosenKo.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosenKo[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
