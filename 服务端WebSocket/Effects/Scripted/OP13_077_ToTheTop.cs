using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-077 到顶点去吧!!（事件，暗）
/// 【主要】可以将我方的 3 张咚!! 转为休息状态：我方场上存在被赋予中的咚!! 的场合，
///         将对方最多 1 张原本的力量不高于 4000 的角色，和对方最多 1 张原本的力量不高于 3000 的角色 KO。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 3 张活跃咚转为休息状态（需有 ≥3 张活跃咚才能支付）。
///     条件 = 我方费用区存在被赋予中（Attached）的咚。两次 KO 各自最多 1 张，按"原本力量"(Info.Power) 过滤。
///     第二次 KO 的候选会排除已被第一次 KO 选中的角色（避免重复选同一张）。
///   - 【反击】= 本次战斗我方领袖 +3000。
/// </summary>
public class OP13_077_ToTheTop : IScriptedEffect
{
    public string CardNumber => "OP13-077";

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
        // 成本：将我方 3 张活跃咚转为休息状态（不足 3 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 3) return;

        // 条件：我方场上存在被赋予中的咚!!
        bool hasAttachedDon = me.CostArea.Any(d => d.State == DonState.Attached);
        if (!hasAttachedDon) return;

        // 无任何可 KO 目标（连原本力量≤4000 的角色都没有）时不提供发动，避免白白支付 3 咚成本
        if (!opp.Characters.Any(c => c.Info.Power <= 4000)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-077【主要】：将我方 3 张咚!! 转为休息状态，KO 对方最多 1 张原本力量≤4000 + 最多 1 张原本力量≤3000 的角色？");
        if (!use) return;

        // 支付成本：3 张活跃咚转休息
        for (int i = 0; i < 3; i++) activeDons[i].State = DonState.Rest;

        // 第一次 KO：对方原本力量不高于 4000 的角色，最多 1 张
        CardInstance? first = null;
        var cand1 = opp.Characters.Where(c => c.Info.Power <= 4000).ToList();
        if (cand1.Count > 0)
        {
            var chosen1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张原本力量≤4000 的对方角色 KO",
                cand1.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen1.Count > 0)
                first = cand1.FirstOrDefault(c => c.Id.ToString() == chosen1[0]);
        }

        // 第二次 KO：对方原本力量不高于 3000 的角色，最多 1 张（排除第一次已选中的那张）
        var cand2 = opp.Characters.Where(c => c.Info.Power <= 3000 && c != first).ToList();
        CardInstance? second = null;
        if (cand2.Count > 0)
        {
            var chosen2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张原本力量≤3000 的对方角色 KO",
                cand2.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen2.Count > 0)
                second = cand2.FirstOrDefault(c => c.Id.ToString() == chosen2[0]);
        }

        // 结算 KO
        if (first is not null) AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, first);
        if (second is not null) AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, second);
    }
}
