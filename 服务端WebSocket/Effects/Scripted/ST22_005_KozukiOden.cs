using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST22-005 光月御殿（角色）
/// （1）此角色因对方的效果将要离开场上的场合，可以改为丢弃我方的 2 张手牌，使此角色不离场。
///       —— 置换/防离场（因对方"效果"离场）机制，引擎仅在战斗 KO 时派发置换钩子(OnAllyWillBeKOd)，
///          对"因效果将要离场"无对应通道，此段未实现（见 reason）。
/// （2）【启动主要】【每回合1次】可以将我方的 3 张咚!! 转为休息状态，并将我方 1 张此角色以外的
///       角色放回其持有者的手牌：将此角色转为活跃状态。
///
/// 本脚本实现（2）启动主要部分。
/// 实现说明 / 简化点：
///   - 成本：将 3 张活跃咚转为休息状态 + 将我方 1 张（本角色以外）角色退回手牌。
///   - 收益：将此角色转为活跃状态（ActivateCard）。
///   - 每回合 1 次用 TurnOnceUsed 控制；成本需可支付（活跃咚≥3 且存在其他角色）才发动。
/// </summary>
public class ST22_005_KozukiOden : IScriptedEffect
{
    public string CardNumber => "ST22-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本可支付性：活跃咚≥3 且存在本角色以外的我方角色
        if (me.ActiveDonCount < 3) return;
        var others = me.Characters.Where(c => c.Id != self.Id).ToList();
        if (others.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "光月御殿【启动主要】：将 3 张咚!! 转为休息状态，并退回我方 1 张其他角色到手牌，使此角色转为活跃状态？");
        if (!use) return;

        // 选择要退回手牌的我方角色（本角色以外）
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张本角色以外的我方角色放回手牌",
            others.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count < 1) return; // 未选择 → 成本未支付

        var bounceTarget = others.First(c => c.Id.ToString() == pick[0]);

        // 成本：将 3 张活跃咚转为休息状态
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 3) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }

        // 成本：将所选角色放回其持有者手牌（我方角色 → 我方手牌）
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounceTarget);

        me.TurnOnceUsed.Add(key);

        // 收益：将此角色转为活跃状态
        AtomicOps.ActivateCard(self);
    }
}
