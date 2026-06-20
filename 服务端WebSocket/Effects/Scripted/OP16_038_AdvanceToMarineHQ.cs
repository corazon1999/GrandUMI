using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-038 前进……朝海军总部……（事件，风，因佩尔地狱/草帽一伙）
/// 【主要】可以将我方的 6 张咚!! 转为休息状态：我方场上存在 5 张卡牌名称不同且拥有《因佩尔地狱》
///         特征的角色的场合，将我方所有领袖和角色转为活跃状态。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 6 张活跃咚转为休息状态（不足 6 张则无法发动）。
///   - 条件"场上存在 5 张卡名不同且具《因佩尔地狱》特征的角色"：对具该特征的角色按卡名去重计数 ≥5。
///   - 满足条件后，将我方领袖与全部角色循环 ActivateCard 转为活跃。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP16_038_AdvanceToMarineHQ : IScriptedEffect
{
    public string CardNumber => "OP16-038";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

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
        // 成本：将我方 6 张活跃咚转为休息状态（不足 6 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 6) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP16-038【主要】：将我方 6 张咚!! 转为休息状态？（场上有 5 张卡名不同的《因佩尔地狱》角色时，将我方领袖和角色全部活跃）");
        if (!use) return;

        // 支付成本：6 张活跃咚转休息
        for (int i = 0; i < 6; i++) activeDons[i].State = DonState.Rest;

        // 条件：场上存在 5 张卡名不同且具《因佩尔地狱》特征的角色
        int distinctNames = me.Characters
            .Where(c => c.Info.HasKeyword("因佩尔地狱"))
            .Select(c => c.Info.Name)
            .Distinct()
            .Count();
        if (distinctNames < 5) return;

        // 将我方领袖与所有角色转为活跃状态
        AtomicOps.ActivateCard(me.Leader);
        foreach (var c in me.Characters) AtomicOps.ActivateCard(c);
    }
}
