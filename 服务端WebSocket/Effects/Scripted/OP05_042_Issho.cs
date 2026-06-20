using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-042 一生（角色）
/// 【登场时】直到下个我方的回合开始时为止，对方最多1张费用不高于7的角色无法攻击。
///
/// 说明：
///   - "无法攻击" 用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)。
///   - "直到下个我方回合开始时" 用 UntilNextOpponentEndPhase 时长近似表达。
///   - 费用按 CurrentCost() 评估。
/// </summary>
public class OP05_042_Issho : IScriptedEffect
{
    public string CardNumber => "OP05-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 7).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多1张费用≤7的对方角色，使其无法攻击（直到下个我方回合开始时）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
