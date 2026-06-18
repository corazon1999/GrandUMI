using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-061 蒙奇·D·路飞（角色 / 光 / 艾格赫德·四皇·草帽一伙，cost10 power12000）
/// 静态：我方生命卡牌不多于 1 张的场合，手牌中的此卡牌费用-1。
///   （由 Effects/HandStaticCost.cs 的静态费用通道按 Number=="EB04-061" 处理，本脚本不重复实现。）
/// 【登场时】可以丢弃我方的 1 张手牌：
///   直到下个对方的结束阶段结束时为止，我方领袖力量 +2000。
///   之后，直到下个对方的结束阶段结束时为止，此角色获得【阻挡者】效果。
///
/// 实现说明 / 简化点：
///   - "可以丢弃 1 张手牌"为可选成本：先 ConfirmOptional；手牌为空则无法支付，直接返回。
///   - 成本支付后让玩家选择丢弃 1 张手牌（DiscardHand）。
///   - 领袖力量 +2000："直到下个对方结束阶段"的精确力量时效引擎无对应字段，
///     按既有惯例（参见 OP13-026 小阳光）用 AddPowerThisTurn 近似（本回合内有效）。
///   - 此角色获得【阻挡者】：用 GiveKeyword(KeywordDuration.UntilNextOpponentEndPhase) 精确表达时长。
/// </summary>
public class EB04_061_Luffy : IScriptedEffect
{
    public string CardNumber => "EB04-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Hand.Count == 0) return; // 无手牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【登场时】：丢弃 1 张手牌，使我方领袖力量+2000，并使此角色获得【阻挡者】？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count < 1) return; // 未完成丢弃 → 成本未支付
        var discard = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        // 效果 1：我方领袖力量 +2000（近似：本回合内有效，见类注释）
        AtomicOps.AddPowerThisTurn(me.Leader, 2000);

        // 效果 2：此角色获得【阻挡者】，直到下个对方的结束阶段结束时为止
        AtomicOps.GiveKeyword(self, "阻挡者", KeywordDuration.UntilNextOpponentEndPhase);
    }
}
