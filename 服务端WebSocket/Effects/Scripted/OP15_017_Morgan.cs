using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-017 摩根（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【启动主要】【每回合1次】可以赋予对方1张角色1张对方休息状态的咚!!：
///   赋予1张领袖或角色最多1张其持有者的休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将对方费用区中1张休息咚贴到对方1张角色上"。该成本无对应 DSL cost 通道，故脚本实现：
///     用 AttachDonFromCost(对方PlayerState, 对方角色Id, 1, DonState.Rest) 实现（贴的是对方休息咚）。
///   - 收益"赋予1张领袖或角色最多1张其持有者的休息状态的咚!!"：
///     目标可为任一方领袖/角色；"其持有者的休息状态的咚"即用目标所属方费用区的休息咚来贴。
///   - 成本需要"对方有休息咚且对方有角色"才能支付，无法满足时不发动。
/// </summary>
public class OP15_017_Morgan : IScriptedEffect
{
    public string CardNumber => "OP15-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前置条件：对方有休息咚 且 对方有角色
        if (opp.RestDonCount < 1 || opp.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "摩根【启动主要】：赋予对方1张角色1张对方休息咚，作为成本；之后赋予1张领袖或角色最多1张其持有者的休息咚？");
        if (!use) return;

        // 成本：赋予对方1张角色1张对方休息状态的咚!!
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "成本：选择1张对方角色，赋予1张对方休息状态的咚!!",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count < 1) return; // 未支付成本
        var costTarget = opp.Characters.First(c => c.Id.ToString() == costPick[0]);
        int paid = AtomicOps.AttachDonFromCost(opp, costTarget.Id, 1, DonState.Rest);
        if (paid < 1) return; // 成本未成功支付

        me.TurnOnceUsed.Add(key);

        // 收益：赋予1张领袖或角色最多1张其持有者的休息状态的咚!!
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        targets.Add(opp.Leader);
        targets.AddRange(opp.Characters);

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LeaderOrCharacter",
            "选择1张领袖或角色，赋予最多1张其持有者的休息状态的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count < 1) return;
        var target = targets.First(c => c.Id.ToString() == pick[0]);

        // "其持有者"：目标若是我方卡用 me，否则用 opp
        bool ownedByMe = me.Leader.Id == target.Id || me.Characters.Any(c => c.Id == target.Id);
        var holder = ownedByMe ? me : opp;
        AtomicOps.AttachDonFromCost(holder, target.Id, 1, DonState.Rest);
    }
}
