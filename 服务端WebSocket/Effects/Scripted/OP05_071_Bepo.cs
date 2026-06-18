using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-071 贝宝（角色，暗）
/// 【攻击时】对方场上咚!!的张数多于我方场上咚!!的张数的场合，本回合中，对方最多 1 张角色力量 -2000。
///
/// 实现说明：
///   - “对方场上咚!!多于我方”用费用区咚!!总数比较：opp.TotalDonInCostArea &gt; me.TotalDonInCostArea。
///   - 由我方选择对方最多 1 张角色，本回合力量 -2000（AddPowerThisTurn 负值）。
/// </summary>
public class OP05_071_Bepo : IScriptedEffect
{
    public string CardNumber => "OP05-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opp.TotalDonInCostArea <= me.TotalDonInCostArea) return;

        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
