using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-067 Ms.圣诞快乐（多洛菲）（角色 / 水）
/// 【阻挡者】（关键词，由引擎处理）
/// 【触发】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：此卡牌登场。
///
/// 实现：OnLifeRevealTrigger，可选支付成本（咚!!-1）后将此卡从废弃区登场。
/// </summary>
public class OP04_067_MsMerryChristmas : IScriptedEffect
{
    public string CardNumber => "OP04-067";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.CostArea.Count == 0) return;                                  // 无咚可放回，无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Ms.圣诞快乐：咚!!-1（将我方1张咚放回咚卡组），使此卡登场？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
