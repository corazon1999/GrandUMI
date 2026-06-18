using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-041 前进·梅利号（舞台 / 暗 / 草帽一伙，cost1）
/// 【登场时】我方领袖拥有《草帽一伙》特征的场合，抽取1张卡牌。
/// 【启动主要】可以将此舞台转为休息状态：我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   直到下个对方的回合结束时为止，我方最多1张拥有《草帽一伙》特征的角色费用+2。
///
/// 实现说明：
///   - 登场时：领袖含《草帽一伙》→ 抽1张。
///   - 启动主要：成本"将此舞台转为休息状态"（RestCard 自身，须为活跃）；收益条件 我方咚总数 ≤ 对方咚总数；
///     满足则选我方1张《草帽一伙》角色，AddCostModifier(+2, UntilNextOpponentEndPhase)（持续到下个对方回合结束）。
/// </summary>
public class EB02_041_GoingMerry : IScriptedEffect
{
    public string CardNumber => "EB02-041";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.Leader.Info.HasKeyword("草帽一伙"))
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // 【启动主要】成本：将此舞台转为休息状态（须为活跃）
        if (ctx.Source.IsTapped) return;

        var cands = me.Characters.Where(c => c.Info.HasKeyword("草帽一伙")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "前进·梅利号【启动主要】：横置此舞台，满足条件则1张《草帽一伙》角色费用+2（至下个对方回合结束）?");
        if (!use) return;

        // 成本：横置此舞台
        AtomicOps.RestCard(ctx.Source);

        // 条件：我方咚数 ≤ 对方咚数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        // 收益：选我方1张《草帽一伙》角色，费用+2 直到下个对方回合结束
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多1张《草帽一伙》角色，费用+2",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(tgt, 2, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
