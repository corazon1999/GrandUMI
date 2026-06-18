using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-030 甚平（角色 / 水 / 鱼人族·王下七武海·太阳海盗团，cost4 power5000）
/// 【KO时】将最多 1 张费用不高于 3 的角色放置到其持有者的卡组最下方。
///
/// 实现说明：
///   - 对象为敌我双方任意费用≤3 的角色：将双方候选合并到同一选择列表。
///   - 选定后按该卡所属方调用 ReturnFieldToDeckBottom（放回其各自持有者的卡组底）。
///   - 费用判定走 CurrentCostOf（含持续费用修正）。
/// </summary>
public class P_030_Jinbe : IScriptedEffect
{
    public string CardNumber => "P-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var mine = me.Characters.Where(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 3).ToList();
        var theirs = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3).ToList();

        var all = new List<CardInstance>();
        all.AddRange(mine);
        all.AddRange(theirs);
        if (all.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤3 的角色（双方任意）放置到其持有者的卡组最下方",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = all.First(c => c.Id.ToString() == chosen[0]);
        int ownerIdx = mine.Contains(tgt) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ownerIdx, tgt);
    }
}
