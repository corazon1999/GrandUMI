using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-029 巴索罗缪·大熊
/// 【登场时】直到下个对方的结束阶段结束时为止，对方最多1张费用不高于5的角色无法转为休息状态。
///
/// "无法转为休息状态" — 即禁止此角色在受到效果时变成 IsTapped=true
/// 用 AtomicOps.AddRestriction(CannotBeRested, UntilNextOpponentEndPhase)，
/// RestCard 会对其 no-op，并由快照下发 cannotBeRested 显示图标（与 OP11-034/OP16-032 同机制）。
/// </summary>
public class OP15_029_Kuma : IScriptedEffect
{
    public string CardNumber => "OP15-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var candidates = opp.Characters
            .Where(c => c.Info.Cost <= 5)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe5",
            "选择对方最多 1 张费用不高于 5 的角色（无法转为休息状态，至下个对方结束阶段结束时为止）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddRestriction(target, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
