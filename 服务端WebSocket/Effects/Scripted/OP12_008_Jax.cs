using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-008 杰克斯（4 费 6000）
/// 【阻挡者】（静态关键字，由卡牌数据驱动，无需脚本实现）
/// 【对方的攻击时】【每回合1次】可以丢弃我方的 1 张手牌：
///   本回合中，对方最多 1 张领袖或角色力量 -2000。
///
/// 实现：对方攻击时触发，每回合限 1 次。先询问是否发动；发动则强制弃 1 张手牌
/// 作为代价，然后从对方领袖或角色中最多选 1 张，本回合力量 -2000。
/// 弃牌候选经 extra.choiceCards 下发卡面。
/// </summary>
public class OP12_008_Jax : IScriptedEffect
{
    public string CardNumber => "OP12-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = "OP12-008-OncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 代价需要至少 1 张手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "杰克斯：弃 1 张手牌，使对方最多 1 张领袖或角色本回合力量 -2000？");
        if (!use) return;

        // 支付代价：弃 1 张手牌（玩家自选）
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "丢弃 1 张手牌作为代价",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var toDiscard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand.FirstOrDefault();
        if (toDiscard is null) return;
        AtomicOps.DiscardHand(me, toDiscard);

        // 标记本回合已用（代价已付，效果是否选到目标都算用过）
        me.TurnOnceUsed.Add(key);

        // 效果：对方领袖或角色最多 1 张力量 -2000
        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选最多 1 张对方领袖或角色，本回合力量 -2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AddPowerThisTurn(target, -2000);
    }
}
