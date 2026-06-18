using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-076 罗布·鲁兹（领航 / 地）
/// 【我方的回合中】【每回合1次】可以丢弃我方的2张手牌：当对方的角色被KO时，将此领袖转为活跃状态。
///
/// 实现：监听 OnAnyCharKOd。仅我方回合、被KO者为对方角色、每回合1次。
/// "可以丢弃2张手牌"为成本：ConfirmOptional 同意后选2张手牌弃掉，再将领袖活跃。
/// </summary>
public class OP03_076_RobLucci : IScriptedEffect
{
    public string CardNumber => "OP03-076";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;                     // 仅我方回合
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != 1 - ctx.OwnerIndex) return;                                        // 对方角色被KO
        var key = "OP03-076-kod" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;                                      // 每回合1次
        if (me.Hand.Count < 2) return;                                                  // 成本需2张手牌

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗布·鲁兹：丢弃2张手牌，将此领袖转为活跃状态？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃2张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (chosen.Count < 2) return;
        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }

        me.TurnOnceUsed.Add(key);
        AtomicOps.ActivateCard(self);
    }
}
