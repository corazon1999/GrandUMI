using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-059 战略调整，高调大作战……（事件，水，因佩尔地狱/巴奇海盗团）
/// 【主要】可以将我方的 7 张咚!! 转为休息状态：确认我方卡组最上方的 5 张卡牌，
///         将其中最多 2 张力量不高于 6000 且拥有《因佩尔地狱》特征的角色卡牌登场。
///         之后，将剩余的卡牌自选顺序放回卡组最下方。
/// 【反击】本次战斗中，我方领袖力量+3000。
///
/// 实现说明 / 简化点：
///   - 【主要】可选成本 = 将我方 7 张活跃咚转为休息状态（不足 7 张则无法发动）。
///   - 看顶 5 张，从中选最多 2 张（力量≤6000 且具《因佩尔地狱》特征）的角色登场。
///   - 无论有无候选都展示全部顶牌，剩余卡牌按玩家选择的顺序放回卡组底。
///   - 客户端通过 prompt 的 extra.choiceCards 显示卡组牌的卡面。
/// </summary>
public class OP16_059_StrategicAdjustment : IScriptedEffect
{
    public string CardNumber => "OP16-059";

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
        // 成本：将我方 7 张活跃咚转为休息状态（不足 7 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 7) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP16-059【主要】：将我方 7 张咚!! 转为休息状态？（确认卡组顶 5 张，最多登场 2 张力量≤6000 的《因佩尔地狱》角色）");
        if (!use) return;

        // 支付成本：7 张活跃咚转休息
        for (int i = 0; i < 7; i++) activeDons[i].State = DonState.Rest;

        await DeckTopCharacterPlay.Resolve(ctx, 5, 2,
            card => card.Info.Kind == CardKind.Character && card.Info.Power <= 6000
                && card.Info.HasKeyword("因佩尔地狱"),
            "确认卡组顶5张，登场最多2张力量不高于6000的《因佩尔地狱》角色");
    }
}
