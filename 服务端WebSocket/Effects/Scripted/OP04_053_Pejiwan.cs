using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-053 佩吉旺（角色 / 水）
/// 【咚!!×1】【每回合1次】当我方发动事件时，抽取1张卡牌。之后，将我方的1张手牌放回卡组最下方。
///
/// 实现：监听 OnOppEventPlayed（对所有监听卡派发，payload owner=出牌方）；仅在 owner==自己
/// （我方发动事件）、此角色被赋予咚≥1、每回合1次时：抽1张，再选1张手牌放回卡组底（必须）。
/// </summary>
public class OP04_053_Pejiwan : IScriptedEffect
{
    public string CardNumber => "OP04-053";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return;                                            // 我方发动事件
        if (me.AttachedDonCount(self.Id) < 1) return;                                   // 咚!!×1
        var key = "OP04-053-event" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;                                      // 每回合1次
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将1张手牌放回卡组最下方", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is not null) AtomicOps.ReturnHandToDeckBottom(me, card);
    }
}
