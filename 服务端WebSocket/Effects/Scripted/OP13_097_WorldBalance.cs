using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-097 世界的均衡……是不可能永远维持下去的。（事件，地）
/// 【主要】可以将我方的 5 张咚!! 转为休息状态：我方场上的角色仅为拥有《天龙人》特征的角色的场合，
///           将对方最多 1 张原本的费用不高于 6 的角色 KO。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 5 张活跃咚转为休息状态（活跃咚不足 5 张则无法发动）。
///     条件 = 我方场上所有角色均拥有《天龙人》特征（场上无角色亦满足）。
///     效果 = 将对方最多 1 张原本费用 ≤6 的角色 KO（min=0）。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_097_WorldBalance : IScriptedEffect
{
    public string CardNumber => "OP13-097";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 可选成本：将我方 5 张活跃咚转为休息状态
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 5) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-097【主要】：将我方 5 张咚!! 转为休息状态，若我方场上角色仅为《天龙人》，将对方最多 1 张原本费用≤6 的角色 KO？");
        if (!use) return;

        // 支付成本：5 张活跃咚转休息
        for (int i = 0; i < 5; i++) activeDons[i].State = DonState.Rest;

        // 条件：我方场上所有角色均拥有《天龙人》特征
        if (!me.Characters.All(c => c.Info.HasKeyword("天龙人"))) return;

        // 效果：将对方最多 1 张原本费用 ≤6 的角色 KO
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用≤6 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null) AtomicOps.KO(s, oppIdx, target);
    }
}
