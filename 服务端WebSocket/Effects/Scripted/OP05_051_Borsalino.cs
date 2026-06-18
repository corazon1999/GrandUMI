using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-051 波尔萨利诺（角色）
/// 【登场时】将最多 1 张费用不高于 4 的角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 目标可为我方或对方任一费用≤4 的角色，故合并双方候选并记录各自持有者下标。
///   - "放回其持有者卡组最下方" → ReturnFieldToDeckBottom(ctx.State, 该角色持有者下标, card)。
///   - "最多 1 张" → ChooseCards(min=0, max=1)。
/// </summary>
public class OP05_051_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP05-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 合并双方费用≤4 的角色，记录其持有者下标
        var cands = new List<(CardInstance card, int ownerIdx)>();
        foreach (var c in me.Characters)
            if (ctx.State.CurrentCostOf(c) <= 4) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters)
            if (ctx.State.CurrentCostOf(c) <= 4) cands.Add((c, 1 - ctx.OwnerIndex));

        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多 1 张费用≤4 的角色，放回其持有者的卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);

        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.ownerIdx, picked.card);
        }
    }
}
