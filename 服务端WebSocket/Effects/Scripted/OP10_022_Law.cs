using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-022 特拉法尔加·罗（领航 / 风·光 / 德莱斯罗兹・超新星・心脏海盗团）
/// 【咚!!×1】【启动主要】【每回合1次】我方角色费用合计为 5 或更高的场合，
///   可以将我方 1 张角色放回其持有者的手牌：
///   公开我方生命区最上方的 1 张卡牌，该卡牌为费用不高于 5 且拥有《超新星》特征的角色卡牌的场合，
///   可以将其登场。
///
/// 实现说明：
///   - 【咚!!×1】发动门槛：此领航身上附着咚!! ≥ 1。
///   - 【每回合1次】用 TurnOnceUsed 标记。
///   - 条件：我方角色当前费用合计 ≥ 5（用 CurrentCost 含费用修正）。
///   - 成本（可选）：将我方 1 张角色放回手牌（BounceToHand）。该成本是发动收益的前提，故必须支付。
///   - 效果：公开生命顶 1 张；若为费用≤5 的《超新星》角色，可选登场（PlayFromHandFree 前需先入手；
///     这里直接从生命区取出登场——用 me.LifeArea 移除后 PlayFromHandFree 需卡在手牌，故先入手再登场）。
/// </summary>
public class OP10_022_Law : IScriptedEffect
{
    public string CardNumber => "OP10-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】发动门槛
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 【每回合1次】
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方角色费用合计 ≥ 5
        int totalCost = me.Characters.Sum(c => c.CurrentCost());
        if (totalCost < 5) return;

        // 成本前置：需有可放回手牌的角色
        if (me.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗【启动主要】：将我方 1 张角色放回手牌，公开生命顶 1 张，若为费用≤5 的《超新星》角色可将其登场？");
        if (!use) return;

        // 成本：选择我方 1 张角色放回手牌
        var bounceChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张角色放回手牌",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (bounceChoice.Count == 0) return; // 未支付成本

        var bounce = me.Characters.FirstOrDefault(c => c.Id.ToString() == bounceChoice[0]);
        if (bounce is null) return;
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounce);

        me.TurnOnceUsed.Add(key);

        // 效果：公开生命区最上方 1 张
        if (me.LifeArea.Count == 0) return;
        var lifeTop = me.LifeArea[0];

        bool isPlayable =
            lifeTop.Info.Kind == CardKind.Character &&
            lifeTop.Info.Cost <= 5 &&
            lifeTop.Info.HasKeyword("超新星");

        if (!isPlayable) return; // 不符合条件 → 公开后留在生命区（原位）

        bool play = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"公开的生命牌为《{lifeTop.Info.Name}》（费用≤5 的超新星角色），是否将其登场？");
        if (!play) return;

        // 从生命区取出该卡 → 入手 → 免费登场
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(lifeTop);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, lifeTop);
    }
}
