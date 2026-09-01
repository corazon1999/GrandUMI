using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-029 达斯琪（角色）
/// 【对方的回合中】此角色因对方的效果将要离开场上的场合，可以改为将我方的 1 张卡牌转为休息状态，
///   使此角色不离场。
/// 【启动主要】【每回合1次】可以将我方 2 张卡牌转为休息状态：
///   直到下个对方的结束阶段结束时为止，此角色的力量 +2000。
///
/// 实现说明：
///   - 效果KO走 OnAllyWillBeKOd，非KO效果离场走 OnAllyWillLeaveField；仅对方回合且受害者为自身时可用。
///     成本：将我方1张活跃卡牌转为休息状态；支付后阻止对应离场。
///   - 【启动主要】成本为将我方 2 张活跃卡牌(领袖/角色)转为休息状态；收益为自身力量 +2000。
///     力量加成使用 AddPowerUntilOppEnd，持续到下个对方结束阶段结束。
///   - 每回合 1 次用 TurnOnceUsed 控制。
/// </summary>
public class OP14_029_Tashigi : IScriptedEffect
{
    public string CardNumber => "OP14-029";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger != EffectTrigger.ActivatedMain)
        {
            // 仅在对方的回合中可用
            if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

            bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
            if (!nonKoLeave &&
                (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
            if (victimOwner != ctx.OwnerIndex || victimId != self.Id.ToString()) return;

            if (AtomicOps.RestableCount(ctx.State, me) < 1) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "达斯琪：是否将我方 1 张卡牌转为休息状态，使此角色不离场？");
            if (!use) return;

            if (!await AtomicOps.PromptRestOwnCards(ctx, 1,
                "将我方 1 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;
            if (nonKoLeave) ctx.State.MarkPreventLeave(self.Id);
            else ctx.State.MarkPreventKO(self.Id);
            return;
        }

        // ── ActivatedMain：每回合 1 次 ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (AtomicOps.RestableCount(ctx.State, me) < 2) return; // 不足 2 张活跃可休置项无法支付成本

        bool act = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "达斯琪【启动主要】：将我方 2 张卡牌转为休息状态，使此角色力量 +2000？");
        if (!act) return;

        if (!await AtomicOps.PromptRestOwnCards(ctx, 2,
            "将我方 2 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;

        AtomicOps.AddPowerUntilOppEnd(self, 2000, ctx.OwnerIndex);
        me.TurnOnceUsed.Add(key);
    }
}
