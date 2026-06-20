using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-112 S-蛇女（角色，光）
/// 【登场时】直到下个对方的回合结束时为止，对方最多 1 张"蒙奇·D·路飞"以外的
///           费用不高于 6 的角色无法攻击。
///
/// 实现说明：
///   - 候选 = 对方角色中费用 ≤6 且名称不为"蒙奇·D·路飞"。
///   - 限制 = CannotAttack，持续至下个对方回合结束（UntilNextOpponentEndPhase）。
/// </summary>
public class OP08_112_SSnake : IScriptedEffect
{
    public string CardNumber => "OP08-112";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(c) <= 6 && !c.MatchesName("蒙奇·D·路飞"))
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤6 的角色（路飞除外），直到下个对方回合结束无法攻击",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
