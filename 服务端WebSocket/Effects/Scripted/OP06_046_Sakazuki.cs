using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-046 萨卡斯基（角色）
/// 【登场时】将最多 1 张费用不高于 2 的角色放回其持有者的卡组最下方。
/// 目标可为任意一方角色，故合并双方角色为候选，按所属方调用 ReturnFieldToDeckBottom。
/// </summary>
public class OP06_046_Sakazuki : IScriptedEffect
{
    public string CardNumber => "OP06-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var mine = me.Characters.Where(c => c.Info.Cost <= 2).ToList();
        var theirs = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        var cands = new List<CardInstance>();
        cands.AddRange(mine);
        cands.AddRange(theirs);
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多 1 张费用≤2 的角色放回其持有者的卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        int ownerOfTgt = mine.Contains(tgt) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ownerOfTgt, tgt);
    }
}
