using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-036 蒙奇·D·路飞（角色 / 光 / 和之国·草帽一伙，cost3 power4000）
/// 【攻击时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   本回合中，此角色和我方最多 1 张领袖力量 +1000。
///
/// 实现说明：
///   - 可选成本"将我方生命顶或底 1 张入手"：先 ConfirmOptional，无生命牌则无法支付成本；
///     由玩家选择取生命顶/底其一加入手牌。
///   - 收益：此角色本回合 +1000；并可选我方最多 1 张领袖本回合 +1000。
/// </summary>
public class P_036_Luffy : IScriptedEffect
{
    public string CardNumber => "P-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【攻击时】：将我方生命顶/底 1 张加入手牌，此角色与最多 1 张领袖本回合 +1000？");
        if (!use) return;

        // 成本：我方生命顶或底 1 张入手
        int which = me.LifeArea.Count == 1
            ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "将哪一张生命牌加入手牌？",
                new[] { "生命区最上方", "生命区最下方" });
        var lifeCard = which == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 收益：此角色本回合 +1000
        AtomicOps.AddPowerThisTurn(self, 1000);

        // 我方最多 1 张领袖本回合 +1000
        var leaderChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
            "选择我方最多 1 张领袖，本回合力量 +1000",
            new List<string> { me.Leader.Id.ToString() }, 0, 1);
        if (leaderChoice.Count > 0)
            AtomicOps.AddPowerThisTurn(me.Leader, 1000);
    }
}
