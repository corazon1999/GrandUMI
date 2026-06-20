using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-052 波尔·玛丽格鲁德（角色）
/// 【登场时】我方场上存在 2 张或更多拥有《亚马逊·百合》或《九蛇海盗团》特征的角色的场合，
///   将最多 1 张费用不高于 2 的角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 条件按特征过滤计数（含本卡自身，登场时已在场）。
///   - 目标可为双方任一费用≤2 角色，按所属方调用 ReturnFieldToDeckBottom。
/// </summary>
public class OP07_052_BoaMarigold : IScriptedEffect
{
    public string CardNumber => "OP07-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int count = me.Characters.Count(c =>
            c.Info.HasKeyword("亚马逊·百合") || c.Info.HasKeyword("九蛇海盗团"));
        if (count < 2) return;

        var bounceCands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2))
            bounceCands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2))
            bounceCands.Add((c, 1 - ctx.OwnerIndex));
        if (bounceCands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = bounceCands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤2 的角色放回其持有者的卡组最下方",
            bounceCands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var sel = bounceCands.First(t => t.card.Id.ToString() == pick[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, sel.owner, sel.card);
        }
    }
}
