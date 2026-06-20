using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-089 弯月形沙丘（事件 / 水 / 王下七武海・巴洛克工作室，cost3）
/// 【反击】我方领袖拥有《王下七武海》特征的场合，将最多1张费用不高于5的角色放回其持有者的手牌。
///
/// 实现说明：
///   - EventCounter 触发；仅当我方领袖拥有《王下七武海》特征时有收益。
///   - "费用不高于5的角色"目标为任意一方（敌我双方）的角色：从双方场上汇总候选，
///     选中后用 BounceToHand 退回其所属方手牌（按目标所属方下标）。
/// </summary>
public class OP01_089_CrescentDune : IScriptedEffect
{
    public string CardNumber => "OP01-089";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 我方领袖须拥有《王下七武海》特征
        if (!me.Leader.Info.HasKeyword("王下七武海")) return;

        // 汇总双方费用≤5 的角色候选
        var cands = new List<CardInstance>();
        cands.AddRange(me.Characters.Where(c => c.Info.Cost <= 5));
        cands.AddRange(opp.Characters.Where(c => c.Info.Cost <= 5));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多1张费用≤5的角色放回其持有者的手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        int ownerOfTgt = me.Characters.Contains(tgt) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        AtomicOps.BounceToHand(ctx.State, ownerOfTgt, tgt);
    }
}
