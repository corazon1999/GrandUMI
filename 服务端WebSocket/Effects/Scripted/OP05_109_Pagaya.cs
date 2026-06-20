using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-109 帕加亚（角色 / 光）
/// 【每回合1次】当发动【触发】时，抽取2张卡牌，丢弃我方的2张手牌。
/// 实现：OnTriggerActivated（生命【触发】发动后派发，payload owner=发动方）。仅本方发动、每回合1次时：抽2弃2。
/// </summary>
public class OP05_109_Pagaya : IScriptedEffect
{
    public string CardNumber => "OP05-109";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnTriggerActivated;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return;                         // 仅本方发动【触发】
        var key = "OP05-109-trig" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;                   // 每回合1次
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        int dn = Math.Min(2, me.Hand.Count);
        if (dn <= 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", $"丢弃{dn}张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), dn, dn);
        foreach (var id in ch)
        {
            var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == id);
            if (c is not null) AtomicOps.DiscardHand(me, c);
        }
    }
}
