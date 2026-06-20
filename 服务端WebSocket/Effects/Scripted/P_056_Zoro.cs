using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-056 罗罗诺亚·佐罗（角色 / 水 4 费 6000 / 草帽一伙）
/// 【登场时】②（可以将费用区中指定数量的咚‼转为休息状态）：
///   将最多1张费用不高于5的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 成本：将费用区 2 张活跃咚!! 转为休息状态（"可以"型可选）。需活跃咚 ≥2。
///   - 收益：将最多 1 张当前费用 ≤5 的角色（双方均可，按当前费用判定）放回其持有者手牌。
///   - 用 CurrentCostOf 取当前费用；BounceToHand 退回手牌。
/// </summary>
public class P_056_Zoro : IScriptedEffect
{
    public string CardNumber => "P-056";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本要求：至少 2 张活跃咚!!
        if (me.ActiveDonCount < 2) return;

        // 收集双方当前费用 ≤5 的角色
        var cands = new List<(int side, CardInstance card)>();
        foreach (var c in me.Characters)
            if (ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 5) cands.Add((ctx.OwnerIndex, c));
        foreach (var c in opp.Characters)
            if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 5) cands.Add((1 - ctx.OwnerIndex, c));
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佐罗【登场时】：横置 2 张活跃咚!!，将最多 1 张费用≤5 的角色放回其持有者手牌？");
        if (!use) return;

        // 成本：将 2 张活跃咚!! 转为休息状态
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return; // 成本未能完整支付（理论上不会发生，前面已校验）

        // 收益：将最多 1 张费用≤5 角色放回其持有者手牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤5 的角色放回其持有者手牌",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, picked.side, picked.card);
        }
    }
}
