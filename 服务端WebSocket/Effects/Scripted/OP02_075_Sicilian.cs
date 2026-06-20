using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-075 西奇（角色 / 暗）
/// 【触发】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）。
///
/// 实现：OnLifeRevealTrigger，可选将我方1张咚放回咚卡组（ReturnDonToDeck）。触发已由揭示阶段确认发动。
/// </summary>
public class OP02_075_Sicilian : IScriptedEffect
{
    public string CardNumber => "OP02-075";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.CostArea.Count == 0) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "西奇：咚!!-1（将我方1张咚放回咚卡组）？");
        if (!use) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
    }
}
