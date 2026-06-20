using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-075 干上一场吧!正是因为活着，才要『厮杀』啊!!（事件，暗）
/// 【主要】可以将我方的 1 张咚!! 转为休息状态：我方领袖为“高路·D·罗杰”，且我方场上存在被赋予中的咚!!的场合，
///         从咚!!卡组中追加最多 1 张休息状态的咚!!。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明 / 简化点：
///   - 【主要】可选成本 = 将我方 1 张活跃咚转为休息状态（无活跃咚则无法支付）。
///   - 结果条件 = 领袖名为“高路·D·罗杰”且费用区存在被赋予中(Attached)的咚!!。
///     仅当条件满足时才从咚!!卡组追加 1 张休息状态咚（RefreshDonFromDeck，rest）。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_075_LetsFight : IScriptedEffect
{
    public string CardNumber => "OP13-075";

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
        // 成本：将我方 1 张活跃咚转为休息状态（无活跃咚则无法发动）
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-075【主要】：将我方 1 张咚!! 转为休息状态，从咚!!卡组追加最多 1 张休息状态的咚!!？");
        if (!use) return;

        // 支付成本：1 张活跃咚转休息
        activeDon.State = DonState.Rest;

        // 结果条件：领袖为“高路·D·罗杰”，且场上存在被赋予中的咚!!
        bool leaderOk = me.Leader.Info.NameIs("高路·D·罗杰");
        bool hasAttachedDon = me.CostArea.Any(d => d.State == DonState.Attached);
        if (!leaderOk || !hasAttachedDon) return;

        // 从咚!!卡组追加 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
