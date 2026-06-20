using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-031 温思默克·丽久（角色 / 暗 / 温思默克家・GERMA 66，cost5 power5000）
/// 【我方的回合中】【登场时】咚!!-1：我方领袖为"山智"的场合，
///   发动我方废弃区中最多 1 张费用不高于 7 的事件的【主要】效果。
///
/// 实现说明（M4 发动废弃区事件主要）：
///   - 登场时（必在我方回合）；可选支付咚!!-1（ConfirmOptional + ReturnDonToDeck）。
///   - 领袖名为"山智"才发动；从废弃区选 ≤1 张费用≤7 事件，
///     用 EffectRuntime.Resolve(..., EventMain) 直接发动其【主要】效果（卡仍留废弃区）。
/// </summary>
public class EB03_031_VinsmokeReiju : IScriptedEffect
{
    public string CardNumber => "EB03-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return; // 【我方的回合中】
        if (!me.Leader.Info.NameIs("山智")) return;                 // 领袖须为"山智"
        if (me.CostArea.Count < 1) return;                          // 无法支付咚!!-1

        var cands = me.Trash.Where(c => c.Info.Kind == CardKind.Event && c.Info.Cost <= 7).ToList();
        if (cands.Count == 0) return;

        bool pay = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "温思默克·丽久【登场时】：支付咚!!-1，发动废弃区中 1 张费用≤7 事件的【主要】效果？");
        if (!pay) return;

        // 支付成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashEvent",
            "选择废弃区中最多 1 张费用≤7 的事件，发动其【主要】效果",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var ev = cands.First(c => c.Id.ToString() == chosen[0]);
        await EffectRuntime.Resolve(ctx.State, ctx.OwnerIndex, ev, EffectTrigger.EventMain, ctx.Prompts);
    }
}
