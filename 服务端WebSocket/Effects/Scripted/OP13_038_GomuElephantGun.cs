using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-038 橡皮橡皮象枪（2 费，事件，风，超新星/草帽一伙）
/// 【主要】将对方最多 1 张费用不高于 5 的角色转为休息状态。
///   之后，当本回合结束时，将我方最多 2 张咚!! 转为活跃状态。
/// 【触发】将对方最多 1 张费用不高于 5 的角色转为休息状态。
///
/// 实现：
///   - EventMain / OnLifeRevealTrigger：从对方原本费用 ≤5 的角色中选最多 1 张转为休息状态。
///
/// 简化点（未实现）：
///   - 【主要】中"之后，当本回合结束时，将我方最多 2 张咚!! 转为活跃状态"为延迟到回合结束的
///     一次性预约效果。事件卡解决后即进入废弃区，引擎当前没有"本回合结束时"延迟调度钩子
///     （GameState 无延迟动作队列，AdvanceTurn 不回放离场事件源的预约），故此段省略。
///     该延迟咚回充不影响本卡核心的"休息对方角色"功能，已在 summary 注明。
/// </summary>
public class OP13_038_GomuElephantGun : IScriptedEffect
{
    public string CardNumber => "OP13-038";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 候选：对方原本费用 ≤5 的角色
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe5",
            "选择最多 1 张对方费用不高于 5 的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null) AtomicOps.RestCard(target);
    }
}
