using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-040 克洛克达尔（角色）
/// 【登场时】可以将我方的1张咚!!转为休息状态：将最多1张费用不高于2的角色放回其持有者的手牌。
///
/// 实现说明 / 简化点：
///   - 成本"将我方 1 张咚!!转为休息状态"：仅当我方存在 1 张活跃咚时可发动（可选）。
///     用 RestCard 无对应咚操作，这里直接对一张活跃咚置休息。
///   - 目标为"费用不高于 2 的角色"，可为敌我任一方，候选合并双方角色，按所属方退回手牌。
/// </summary>
public class OP07_040_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP07-040";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本需要至少 1 张活跃咚
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        // 候选：双方费用≤2 的角色
        var myCands = me.Characters.Where(c => c.Info.Cost <= 2).ToList();
        var oppCands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (myCands.Count == 0 && oppCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔【登场时】：将我方 1 张咚!!转为休息状态，将最多 1 张费用≤2 的角色放回其持有者手牌？");
        if (!use) return;

        // 支付成本：将一张活跃咚转为休息状态
        activeDon.State = DonState.Rest;

        var all = new List<CardInstance>();
        all.AddRange(myCands);
        all.AddRange(oppCands);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacterCostLe2",
            "将最多 1 张费用≤2 的角色放回其持有者手牌",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var id = chosen[0];
        if (myCands.Any(c => c.Id.ToString() == id))
            AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, myCands.First(c => c.Id.ToString() == id));
        else
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, oppCands.First(c => c.Id.ToString() == id));
    }
}
