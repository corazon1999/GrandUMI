using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-038 多谢款待♡（事件 / 暗 / 温思默克家・GERMA 66）
/// 【主要】可以将我方的 1 张咚!! 转为休息状态：我方场上咚!!的张数不多于对方场上咚!!的张数，
///   且我方角色仅为拥有的特征中包含〈GERMA〉的角色的场合，从咚!!卡组中追加最多 2 张休息状态的咚!!。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 主要段(EventMain)：成本"将我方 1 张活跃咚转为休息状态"(直接改 DonState)，"可以"=可选；
///     收益条件 = 我方咚总数 ≤ 对方咚总数 且 我方所有角色特征均含〈GERMA〉(无角色则视为满足)；
///     满足 → RefreshDonFromDeck(me, 2, Rest)。
///   - 反击段(EventCounter)：我方领袖本次战斗 +3000(AddPowerThisBattle)。
///   - 〈GERMA〉为特征子串(如 "GERMA 66")，用 Keywords.Any(k => k.Contains("GERMA")) 判定。
/// </summary>
public class EB03_038_ThanksForTheMeal : IScriptedEffect
{
    public string CardNumber => "EB03-038";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】本次战斗中，我方领袖力量 +3000
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // 【主要】成本：将我方 1 张活跃咚转为休息状态（"可以"=可选）
        if (me.ActiveDonCount < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多谢款待♡【主要】：将我方 1 张咚!! 转为休息状态，满足条件则追加最多 2 张休息咚?");
        if (!use) return;

        // 支付成本：横置 1 张活跃咚
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Active) { d.State = DonState.Rest; break; }
        }

        // 条件：我方咚数 ≤ 对方咚数，且我方所有角色特征均含〈GERMA〉(无角色则满足)
        bool donOk = me.TotalDonInCostArea <= opp.TotalDonInCostArea;
        bool germaOnly = me.Characters.All(c => c.Info.Keywords.Any(k => k.Contains("GERMA")));

        if (donOk && germaOnly)
            AtomicOps.RefreshDonFromDeck(me, 2, DonState.Rest);
    }
}
