using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-083 光月桃之助（角色 / 紫(地) / 2 费 / 0 / 德莱斯罗兹·和之国·光月家）
/// 【启动主要】可以将此角色和我方 1 张拥有《德莱斯罗兹》特征的领袖或舞台转为休息状态：
///   本回合中，对方最多 1 张角色费用 -2。
///
/// 说明 / 简化点：
///   - 发动成本：将自身转为休息状态，并选定我方 1 张《德莱斯罗兹》特征的领袖或舞台一并转休息。
///     该成本（横置特定特征的领袖/舞台）DSL 的 cost 节无法表达，故用脚本实现。
///   - 仅当存在可作为成本的《德莱斯罗兹》领袖/舞台时才询问发动（否则成本无法支付）。
///   - 收益：对方最多 1 张角色本回合费用 -2（ThisTurn）。
/// </summary>
public class OP10_083_Momonosuke : IScriptedEffect
{
    public string CardNumber => "OP10-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 自身须处于活跃状态才能作为成本转休息
        if (self.IsTapped) return;

        // 收集可作为成本的《德莱斯罗兹》活跃领袖/舞台
        var costTargets = new List<CardInstance>();
        if (me.Leader != null && !me.Leader.IsTapped && me.Leader.Info.HasKeyword("德莱斯罗兹"))
            costTargets.Add(me.Leader);
        if (me.StageCard != null && !me.StageCard.IsTapped && me.StageCard.Info.HasKeyword("德莱斯罗兹"))
            costTargets.Add(me.StageCard);
        if (costTargets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "桃之助【启动主要】：横置自身及我方 1 张《德莱斯罗兹》领袖/舞台，使对方最多 1 张角色本回合费用 -2？");
        if (!use) return;

        // 选定 1 张《德莱斯罗兹》领袖/舞台作为成本
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrStage",
            "选择 1 张《德莱斯罗兹》领袖或舞台转为休息状态",
            costTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;
        var costCard = costTargets.First(c => c.Id.ToString() == pick[0]);

        // 支付成本：横置自身与所选领袖/舞台
        AtomicOps.RestCard(self);
        AtomicOps.RestCard(costCard);

        // 收益：对方最多 1 张角色本回合费用 -2
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合费用 -2",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddCostModifier(tgt, -2, KeywordDuration.ThisTurn);
    }
}
