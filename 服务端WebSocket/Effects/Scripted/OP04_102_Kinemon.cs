using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-102 锦卫门（角色 / 光 / 和之国）
/// 【启动主要】【每回合1次】（可以将费用区中指定数量的咚‼转为休息状态），
///   可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：将此角色转为活跃状态。
///
/// 实现说明：
///   - 每回合 1 次：TurnOnceUsed 记账。
///   - 必付成本=生命区顶或底 1 张入手（用 ChooseOption 选顶/底/不发动）。
///   - 收益=将此角色转为活跃状态（ActivateCard）。
///   - "可以将指定数量的咚转为休息"为括号内可选成本、与收益无文本耦合，按惯例不实现该可选成本(no-op)。
/// </summary>
public class OP04_102_Kinemon : IScriptedEffect
{
    public string CardNumber => "OP04-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (me.LifeArea.Count == 0) return; // 无生命牌则无法支付成本

        var options = new List<string> { "生命区最上方", "生命区最下方", "不发动" };
        int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "锦卫门【启动主要】：将生命区最上方或最下方 1 张加入手牌，将此角色转为活跃状态？", options);
        if (pick == 2) return;

        me.TurnOnceUsed.Add(key);

        // 成本：生命顶或底 1 张入手
        var card = pick == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(card);
        me.Hand.Add(card);

        // 收益：将此角色转为活跃状态
        AtomicOps.ActivateCard(self);
    }
}
