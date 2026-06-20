using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-070 巴法罗（角色 / 暗）
/// 当此角色因对方角色的效果转为休息状态时，可以将我方场上的 1 张咚!! 放回咚!! 卡组。
/// 那样做的场合，将此角色转为活跃状态。【阻挡者】
///
/// 实现说明：
///   - 用 OnCharRested watcher，仅当被横置的是本卡自身时触发。
///   - 引擎 watcher 仅在“因效果转为休息”时派发；无法精确区分是否“对方角色的”效果，
///     此处近似为“因效果被横置且非我方回合”才提示（对方回合中对方效果横置最常见）。
///   - 可选成本：将我方场上 1 张咚!! 放回咚!! 卡组（ReturnDonToDeck）；
///     支付后将本角色转为活跃（ActivateCard）。
///   - 【阻挡者】为纯关键词，由引擎处理，此处不实现。
/// </summary>
public class OP14_070_Buffalo : IScriptedEffect
{
    public string CardNumber => "OP14-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];

        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return; // 仅本卡被横置

        // 近似“因对方角色效果”：限定对方回合中
        if (ctx.State.CurrentTurnPlayer == owner) return;

        // 需要场上至少有 1 张可放回的咚!!（费用区中的咚）
        if (me.CostArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "巴法罗：将我方场上 1 张咚!! 放回咚!! 卡组，并将此角色转为活跃状态？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.ActivateCard(ctx.Source);
    }
}
