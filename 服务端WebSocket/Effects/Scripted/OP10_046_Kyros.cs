using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-046 居鲁士（角色）
/// 【登场时】将最多 1 张费用不高于 5 的角色放回其持有者的手牌。
///
/// 实现说明：
/// - 目标含双方所有角色（不限于我方），费用以卡面原始费 c.Info.Cost 判定（≤5）。
/// - 在 C# 中可合并双方角色为候选并按所属方退回，逐一记录归属，BounceToHand 用目标所属方下标。
/// </summary>
public class OP10_046_Kyros : IScriptedEffect
{
    public string CardNumber => "OP10-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 候选：双方所有费用 ≤5 的角色（记录所属方）
        var cands = new List<(CardInstance card, int ownerIdx)>();
        foreach (var c in me.Characters.Where(c => c.Info.Cost <= 5))
            cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => c.Info.Cost <= 5))
            cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var ids = cands.Select(x => x.card.Id.ToString()).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用不高于 5 的角色放回其持有者的手牌（最多 1 张）",
            ids, 0, 1);
        if (chosen.Count == 0) return;

        var pick = cands.First(x => x.card.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(ctx.State, pick.ownerIdx, pick.card);
    }
}
