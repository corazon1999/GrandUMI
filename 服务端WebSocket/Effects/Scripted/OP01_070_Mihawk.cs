using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-070 杰拉基尔·米霍克（角色 / 水 / 王下七武海）
/// 【登场时】将最多1张费用不高于7的角色放置到其持有者的卡组最下方。
///
/// 实现说明：
///   - "任意一方"的费用≤7角色：合并双方场上角色为候选。
///   - 用持续费用评估 CurrentCostOf 判 ≤7；选中后用 ReturnFieldToDeckBottom（按其所属方下标）放回卡组底。
/// </summary>
public class OP01_070_Mihawk : IScriptedEffect
{
    public string CardNumber => "OP01-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters)
            if (ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 7) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters)
            if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 7) cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多1张费用≤7的角色放到其持有者卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.owner, picked.card);
        }
    }
}
