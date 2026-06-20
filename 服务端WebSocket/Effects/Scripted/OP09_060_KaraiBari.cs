using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-060 卡莱·巴利岛（舞台）
/// 【启动主要】可以将我方的 2 张手牌自选顺序放回卡组最下方，并将此舞台转为休息状态：
///   我方领袖拥有《十字公会》特征的场合，抽取 2 张卡牌。
///
/// 实现说明 / 简化点：
///   - 成本：将 2 张手牌放回卡组最下方（ReturnHandToDeckBottom）+ 将此舞台横置（RestCard）。
///     "自选顺序放回卡组底"按所选顺序逐张放底（对实战影响极小）。
///   - 收益仅在领袖含《十字公会》特征时为抽 2；非该特征时支付成本无收益，故仅在满足特征时询问发动。
///   - 需要手牌 ≥2、舞台为活跃状态才有发动意义。
/// </summary>
public class OP09_060_KaraiBari : IScriptedEffect
{
    public string CardNumber => "OP09-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 收益条件：领袖拥有《十字公会》特征
        if (!me.Leader.Info.HasKeyword("十字公会")) return;
        // 成本前提：手牌 ≥2、舞台未横置
        if (me.Hand.Count < 2 || self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡莱·巴利岛【启动主要】：将 2 张手牌放回卡组底并横置此舞台，抽取 2 张卡牌？");
        if (!use) return;

        // 成本：选择 2 张手牌放回卡组最下方
        var handCands = me.Hand.ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将我方的 2 张手牌放回卡组最下方",
            handCands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (chosen.Count < 2) return; // 成本未支付

        foreach (var id in chosen)
        {
            var card = handCands.First(c => c.Id.ToString() == id);
            AtomicOps.ReturnHandToDeckBottom(me, card);
        }

        // 成本：将此舞台转为休息状态
        AtomicOps.RestCard(self);

        // 收益：抽取 2 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
