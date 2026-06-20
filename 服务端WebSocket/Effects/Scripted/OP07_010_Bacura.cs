using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-010 芭卡拉（角色）
/// 【阻挡者】（引擎处理关键词）
/// 【对方的攻击时】【每回合1次】可以丢弃我方的 1 张手牌：
///   本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///
/// 实现说明：
///   - 触发时机 OnOppAttackDeclare。
///   - 可选成本"丢弃 1 张手牌"用 ConfirmOptional + ChooseCards 选弃，支付后再加力量。
///   - +2000 用 AddPowerThisBattle，目标为我方领袖或角色（最多 1 张）。
/// </summary>
public class OP07_010_Bacura : IScriptedEffect
{
    public string CardNumber => "OP07-010";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 需要有手牌可弃
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "芭卡拉【对方的攻击时】：丢弃 1 张手牌，本次战斗中我方 1 张领袖或角色 +2000？");
        if (!use) return;

        // 成本：丢弃 1 张手牌
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (discard.Count < 1) return; // 未支付成本

        var toDiscard = me.Hand.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, toDiscard);
        me.TurnOnceUsed.Add(key);

        // 收益：本次战斗我方 1 张领袖或角色 +2000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，本次战斗 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }
    }
}
