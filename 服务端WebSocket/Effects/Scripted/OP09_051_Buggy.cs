using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-051 巴奇（角色）
/// 【登场时】将对方最多 1 张角色放回其持有者的卡组最下方。
///   之后，我方场上不存在 5 张费用为 5 或更高的角色的场合，将此角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 第一段为对方角色退回卡组底（ReturnFieldToDeckBottom，目标方下标 1-OwnerIndex）。
///   - 第二段为固定条件判定：统计我方场上当前费用 ≥5 的角色数量，不足 5 张则把自身退回卡组底。
///     费用用 CurrentCost() 评估（含费用修正）。
/// </summary>
public class OP09_051_Buggy : IScriptedEffect
{
    public string CardNumber => "OP09-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 第一段：将对方最多 1 张角色放回其持有者的卡组最下方
        var cands = opp.Characters.ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张角色放回其持有者的卡组最下方",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.ReturnFieldToDeckBottom(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 第二段：我方场上不存在 5 张费用 ≥5 的角色时，将自身放回卡组最下方
        int highCostCount = me.Characters.Count(c => ctx.State.CurrentCostOf(c) >= 5);
        if (highCostCount < 5)
        {
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, self);
        }
    }
}
