using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-037 卡鲁秋（事件）
/// 【主要】可以将我方1张拥有《纯毛族》特征的角色转为休息状态：将对方最多1张角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本"横置我方1张《纯毛族》角色"无 DSL cost 通道，脚本实现：从我方活跃的《纯毛族》角色中选1张横置。
///   - 收益"将对方最多1张角色转为休息状态"用 RestCard。
/// </summary>
public class OP08_037_Carrot : IScriptedEffect
{
    public string CardNumber => "OP08-037";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本候选：我方活跃的《纯毛族》角色
        var costCands = me.Characters.Where(c => !c.IsTapped && c.Info.HasKeyword("纯毛族")).ToList();
        if (costCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡鲁秋【主要】：横置我方1张《纯毛族》角色作为成本，将对方最多1张角色转为休息状态？");
        if (!use) return;

        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "成本：选择我方1张《纯毛族》角色转为休息状态",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count < 1) return;
        var costCard = costCands.First(c => c.Id.ToString() == costPick[0]);
        AtomicOps.RestCard(costCard);

        // 收益：将对方最多1张角色转为休息状态
        var targets = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (targets.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张角色转为休息状态",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
