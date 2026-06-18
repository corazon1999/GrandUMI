using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-057 如果向恶势力屈服，男人就没有活着的尊严了……（事件，水 / 白胡子海盗团）
/// 【主要】可以将我方的 1 张咚!! 转为休息状态：我方生命卡牌不多于 1 张的场合，
///         本回合中，我方领袖将要攻击时，对方无法发动【阻挡者】效果。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】可选成本 = 将我方 1 张活跃咚转为休息状态（无活跃咚则无法发动）。
///     支付后，仅当我方生命卡牌 ≤ 1 张时生效：本回合给我方领袖赋予【不可阻挡】。
///     ActionValidator.CanDeclareBlocker 在攻击者具有【不可阻挡】时拒绝对方阻挡，
///     等价于"我方领袖攻击时对方无法发动【阻挡者】"。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP13_057_DignityOfMan : IScriptedEffect
{
    public string CardNumber => "OP13-057";

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
        // 成本：将我方 1 张活跃咚转为休息状态（不足则无法发动）
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-057【主要】：将我方 1 张咚!! 转为休息状态？（生命≤1 时本回合我方领袖攻击时对方无法发动【阻挡者】）");
        if (!use) return;

        // 支付成本
        activeDon.State = DonState.Rest;

        // 条件：我方生命卡牌不多于 1 张
        if (me.LifeCount > 1) return;

        // 本回合我方领袖攻击时对方无法阻挡（等价【不可阻挡】）
        AtomicOps.GiveKeyword(me.Leader, "不可阻挡", KeywordDuration.ThisTurn);
    }
}
