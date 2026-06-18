using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-057 三・千・世・界（事件）
/// 【主要】将最多 1 张费用不高于 5 的角色放回其持有者的卡组最下方。
/// 【触发】将最多 1 张费用不高于 3 的角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 目标为敌我任意一方的角色，放回各自持有者卡组底（ReturnFieldToDeckBottom）。
///   - 主要费用上限 5，触发上限 3，按 ctx.Trigger 分支。
/// </summary>
public class OP03_057_SanZenSeKai : IScriptedEffect
{
    public string CardNumber => "OP03-057";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        int maxCost = ctx.Trigger == EffectTrigger.OnLifeRevealTrigger ? 3 : 5;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var myCands = me.Characters
            .Where(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= maxCost).ToList();
        var oppCands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= maxCost).ToList();
        var all = new List<CardInstance>();
        all.AddRange(myCands);
        all.AddRange(oppCands);
        if (all.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            $"将最多 1 张费用≤{maxCost} 的角色放回其持有者的卡组最下方",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = all.First(c => c.Id.ToString() == chosen[0]);
        int ownerOfTgt = myCands.Contains(tgt) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ownerOfTgt, tgt);
    }
}
