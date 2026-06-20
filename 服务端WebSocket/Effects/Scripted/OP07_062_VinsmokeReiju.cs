using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-062 温思默克·丽久（角色）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   将我方最多 1 张费用为 1 且拥有《温思默克家》特征的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 条件“我方咚数 ≤ 对方咚数”取费用区咚!!总数比较 me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 目标为我方费用 1 且含《温思默克家》特征的角色，BounceToHand 回手。
/// </summary>
public class OP07_062_VinsmokeReiju : IScriptedEffect
{
    public string CardNumber => "OP07-062";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        var cands = me.Characters.Where(c =>
            ctx.State.CurrentCostOf(c) == 1 && c.Info.HasKeyword("温思默克家")).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将最多 1 张费用 1 且含《温思默克家》特征的我方角色放回手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, tgt);
        }
    }
}
