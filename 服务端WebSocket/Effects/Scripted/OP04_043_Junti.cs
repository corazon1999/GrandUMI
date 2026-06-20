using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-043 润媞（角色 / 水 / 百兽海盗团）
/// 【咚!!×1】【攻击时】将最多 1 张费用不高于 2 的角色放回其持有者的手牌或卡组最下方。
///
/// 实现说明：
///   - 【咚!!×1】= 此角色被赋予中的咚!! ≥1 张时发动。
///   - 候选为双方所有费用≤2 角色（"其持有者"=该角色所属方）。
///   - 由该角色的持有者二选一：放回手牌(BounceToHand) 或 放回卡组最下方(ReturnFieldToDeckBottom)。
/// </summary>
public class OP04_043_Junti : IScriptedEffect
{
    public string CardNumber => "OP04-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 双方费用≤2 角色（记录所属方）
        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters.Where(c => c.CurrentCost() <= 2)) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters.Where(c => c.CurrentCost() <= 2)) cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用≤2 的角色放回其持有者的手牌或卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);

        // 由持有者二选一：放回手牌 或 卡组最下方
        int opt = await ctx.Prompts.ChooseOption(picked.owner,
            "将此角色放回手牌或卡组最下方",
            new[] { "放回手牌", "放回卡组最下方" });
        if (opt == 0)
            AtomicOps.BounceToHand(ctx.State, picked.owner, picked.card);
        else
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.owner, picked.card);
    }
}
