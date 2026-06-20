using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-009 特拉法尔加·罗（角色）
/// 【速攻】（关键词，引擎处理）
/// 【对方的攻击时】【每回合1次】可以丢弃我方的 2 张手牌：
///   选择我方的领袖和 1 张角色，本次战斗中将所选卡牌各自原本的力量互换。
///
/// 实现说明 / 简化点：
///   - "原本的力量"取卡面原始力量 Info.Power（不含咚/修正）。
///   - 互换通过对各自施加 ThisBattle 力量差值实现：领袖 += (角色原力 - 领袖原力)，
///     角色 += (领袖原力 - 角色原力)。结算时两者的"原本力量"即被交换。
///   - 成本"丢弃 2 张手牌"必须支付完成才生效。
/// </summary>
public class OP14_009_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "OP14-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 需要至少 2 张手牌作为成本，且至少有 1 张角色
        if (me.Hand.Count < 2) return;
        if (me.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗【对方的攻击时】：丢弃 2 张手牌，互换我方领袖与 1 张角色本次战斗中的原本力量？");
        if (!use) return;

        // 成本：丢弃 2 张手牌
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方的 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (discard.Count < 2) return; // 成本未支付
        foreach (var cid in discard)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }

        // 选择 1 张我方角色（领袖固定为另一方）
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张我方角色，与领袖互换原本力量",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var chr = me.Characters.First(c => c.Id.ToString() == chosen[0]);

        int leaderBase = me.Leader.Info.Power;
        int charBase = chr.Info.Power;

        // 互换原本力量：本次战斗中各自加上差值
        AtomicOps.AddPowerThisBattle(me.Leader, charBase - leaderBase);
        AtomicOps.AddPowerThisBattle(chr, leaderBase - charBase);

        me.TurnOnceUsed.Add(key);
    }
}
