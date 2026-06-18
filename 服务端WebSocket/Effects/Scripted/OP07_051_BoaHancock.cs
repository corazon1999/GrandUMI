using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-051 波尔·汉库珂（角色）
/// 【登场时】直到下个对方的回合结束时为止，对方最多 1 张“蒙奇·D·路飞”以外的角色无法攻击。
///   之后，将最多 1 张费用不高于 1 的角色放回其持有者的卡组最下方。
///
/// 实现说明 / 简化点：
///   - “无法攻击”用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase) 表达。
///   - 排除名称“蒙奇·D·路飞”用 CardInstance.MatchesName 判断。
///   - 第二段“最多 1 张费用≤1 的角色”可选双方任一角色，按所属方调用 ReturnFieldToDeckBottom。
/// </summary>
public class OP07_051_BoaHancock : IScriptedEffect
{
    public string CardNumber => "OP07-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 段 1：使对方最多 1 张“蒙奇·D·路飞”以外的角色无法攻击
        var noAttackCands = opp.Characters.Where(c => !c.MatchesName("蒙奇·D·路飞")).ToList();
        if (noAttackCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张“蒙奇·D·路飞”以外的对方角色，使其无法攻击（至下个对方回合结束）",
                noAttackCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = noAttackCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
            }
        }

        // 段 2：将最多 1 张费用≤1 的角色放回其持有者的卡组最下方
        var bounceCands = new List<(CardInstance card, int owner)>();
        foreach (var c in ctx.State.Players[ctx.OwnerIndex].Characters.Where(c => ctx.State.CurrentCostOf(c) <= 1))
            bounceCands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 1))
            bounceCands.Add((c, 1 - ctx.OwnerIndex));
        if (bounceCands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = bounceCands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤1 的角色放回其持有者的卡组最下方",
            bounceCands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var sel = bounceCands.First(t => t.card.Id.ToString() == pick[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, sel.owner, sel.card);
        }
    }
}
