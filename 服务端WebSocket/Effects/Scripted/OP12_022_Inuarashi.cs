using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-022 犬岚
/// 【启动主要】可以将此角色转为休息状态：对方最多 1 张处于休息状态且费用不高于 5 的角色，
/// 在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现：以"将此角色转为休息状态"为代价（自身须为活跃状态），
/// 从对方处于休息状态且原本费用 ≤5 的角色中选最多 1 张，标记其下个重置阶段不会转活跃。
/// </summary>
public class OP12_022_Inuarashi : IScriptedEffect
{
    public string CardNumber => "OP12-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 代价：将此角色转为休息状态（自身须为活跃状态）
        if (ctx.Source.IsTapped) return;
        ctx.Source.IsTapped = true;

        // 候选：对方处于休息状态且原本费用 ≤5 的角色
        var candidates = opp.Characters
            .Where(c => c.IsTapped && c.Info.Cost <= 5)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacterCostLe5",
            "选择最多 1 张对方休息状态且费用不高于 5 的角色，使其下个重置阶段不转活跃",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null) target.CannotActivateNextReset = true;
    }
}
