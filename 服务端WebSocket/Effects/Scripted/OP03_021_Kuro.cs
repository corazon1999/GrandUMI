using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-021 克洛（领航 / 风 5 费 5000，东海/黑猫海盗团）
/// 【启动主要】③（将费用区中3张咚!!转为休息状态），可以将我方2张拥有《东海》特征的角色转为休息状态：
///   将此领袖转为活跃状态，并将对方最多1张费用不高于5的角色转为休息状态。
///
/// 实现说明：
///   - 成本：休息费用区3张活跃咚!! + 将我方2张活跃的《东海》角色转为休息状态。
///   - 效果：将此领袖转为活跃状态（ActivateCard）；将对方最多1张当前费用≤5的角色转为休息状态。
/// </summary>
public class OP03_021_Kuro : IScriptedEffect
{
    public string CardNumber => "OP03-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        // 成本前置：≥3张活跃咚!! 且 ≥2张活跃《东海》角色
        var activeDons = me.CostArea
            .Where(d => d.State == DonState.Active && d.AttachedToCardId is null).ToList();
        if (activeDons.Count < 3) return;
        var eastCands = me.Characters.Where(c => c.Info.HasKeyword("东海") && !c.IsTapped).ToList();
        if (eastCands.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛【启动主要】：休息3张咚!!与我方2张《东海》角色，将此领袖转为活跃，并将对方最多1张费用≤5角色转为休息？");
        if (!use) return;

        // 成本：横置我方2张《东海》角色
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "成本：选择我方2张《东海》角色转为休息状态",
            eastCands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (costPick.Count < 2) return;

        // 成本：休息3张活跃咚!!
        for (int i = 0; i < 3; i++) activeDons[i].State = DonState.Rest;
        foreach (var id in costPick)
            AtomicOps.RestCard(eastCands.First(c => c.Id.ToString() == id));

        // 效果：将此领袖转为活跃状态
        AtomicOps.ActivateCard(me.Leader);

        // 效果：将对方最多1张费用≤5的角色转为休息状态
        var koCands = opp.Characters
            .Where(c => !c.IsTapped && ctx.State.CurrentCostOf(oppIdx, c) <= 5).ToList();
        if (koCands.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤5的角色转为休息状态",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
                AtomicOps.RestCard(koCands.First(c => c.Id.ToString() == pick[0]));
        }
    }
}
