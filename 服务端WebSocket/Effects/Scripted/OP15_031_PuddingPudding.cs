using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-031 布丁布丁（角色）
/// 【登场时】选择对方最多1张处于休息状态的角色。所选角色的费用与赋予该角色咚!!的张数相同的场合，将其KO。
///
/// 实现说明：
///   - 候选为对方处于休息状态(IsTapped)的角色。
///   - 判定条件"角色费用 = 该角色被赋予的咚!!张数"：用 target.CurrentCost() 与 opp.AttachedDonCount(target.Id) 比较。
///   - 条件成立则 KO，否则什么也不做（仍消耗了"选择"，与文本一致）。
/// </summary>
public class OP15_031_PuddingPudding : IScriptedEffect
{
    public string CardNumber => "OP15-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张处于休息状态的角色（费用=其被赋予咚!!张数则KO）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count < 1) return;

        var target = cands.First(c => c.Id.ToString() == pick[0]);
        if (ctx.State.CurrentCostOf(target) == opp.AttachedDonCount(target.Id))
        {
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
