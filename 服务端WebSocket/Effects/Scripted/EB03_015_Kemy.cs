using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-015 凯米（角色 / 风 / 人鱼族・鱼人岛）
/// 【启动主要】可以将此角色转为休息状态：
///   赋予我方1张拥有《鱼人族》或《人鱼族》特征的领袖或角色最多1张休息状态的咚!!。
///   之后，将对方最多1张费用不高于2的角色转为休息状态。
///
/// 实现说明：
///   - 成本为横置自身（RestCard）。
///   - 贴咚到指定的《鱼人族》/《人鱼族》领袖或角色：AttachDonFromCost(..., DonState.Rest)。
///   - 之后：对方最多1张费用≤2的角色转为休息状态（RestCard）。
/// </summary>
public class EB03_015_Kemy : IScriptedEffect
{
    public string CardNumber => "EB03-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯米【启动主要】：横置此角色，赋予我方1张《鱼人族》/《人鱼族》领袖或角色1张休息咚!!，并将对方1张费用≤2角色转为休息状态？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 收益1：赋予我方1张《鱼人族》/《人鱼族》领袖或角色最多1张休息状态的咚!!
        var donTargets = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族"))
            donTargets.Add(me.Leader);
        donTargets.AddRange(me.Characters.Where(c =>
            c.Info.HasKeyword("鱼人族") || c.Info.HasKeyword("人鱼族")));
        if (donTargets.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择1张《鱼人族》/《人鱼族》领袖或角色，赋予1张休息状态的咚!!",
                donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = donTargets.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
            }
        }

        // 收益2：对方最多1张费用≤2的角色转为休息状态
        var restCands = opp.Characters.Where(c => c.Info.Cost <= 2 && !c.IsTapped).ToList();
        if (restCands.Count > 0)
        {
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多1张费用≤2的角色转为休息状态",
                restCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch2.Count > 0)
            {
                var tgt = restCands.First(c => c.Id.ToString() == ch2[0]);
                AtomicOps.RestCard(tgt);
            }
        }
    }
}
