using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-030 罗格镇（舞台 / 水 / 东海）
/// 【启动主要】可以将此卡牌和我方的 1 张手牌自选顺序放回卡组最下方：抽取 2 张卡牌。
///
/// 实现说明：
///   - 成本：将此舞台自身 + 我方 1 张手牌放回卡组最下方（"自选顺序"对放底影响极小，按先舞台后手牌处理）。
///   - 需手牌中至少有 1 张可放回（否则无法支付成本）。
///   - 支付成本后抽 2 张。
/// </summary>
public class EB01_030_LoguetownTown : IScriptedEffect
{
    public string CardNumber => "EB01-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 需有 1 张手牌可作为成本
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗格镇【启动主要】：将此舞台与 1 张手牌放回卡组最下方，抽取 2 张卡牌？");
        if (!use) return;

        // 选择作为成本放回的 1 张手牌
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择 1 张手牌放回卡组最下方（成本）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;
        var handCard = me.Hand.First(c => c.Id.ToString() == pick[0]);

        // 成本：此舞台放回卡组最下方
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);
        // 成本：选定手牌放回卡组最下方
        AtomicOps.ReturnHandToDeckBottom(me, handCard);

        // 收益：抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
