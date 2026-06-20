using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-086 青椒（角色 / 地）
/// 【咚!!×1】此角色通过战斗将对方的角色KO时，抽取2张卡牌，丢弃我方的2张手牌。
///
/// 实现：监听 OnAnyCharKOd。仅当 reason=="battle"、被KO者为对方角色、且本次战斗攻击者为此角色
/// （payload attackerId==self），并且此角色被赋予咚≥1 时：抽2张，再弃2张（必须）。
/// </summary>
public class OP04_086_Pepper : IScriptedEffect
{
    public string CardNumber => "OP04-086";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.AttachedDonCount(self.Id) < 1) return;                                   // 咚!!×1
        var reason = ctx.Vars.TryGetValue("reason", out var rv) ? rv as string : null;
        if (reason != "battle") return;                                                 // 仅战斗KO
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != 1 - ctx.OwnerIndex) return;                                        // 对方角色被KO
        var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
        if (attackerId != self.Id.ToString()) return;                                   // 须为此角色发起的战斗

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        int dn = Math.Min(2, me.Hand.Count);
        if (dn <= 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            $"丢弃{dn}张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), dn, dn);
        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
    }
}
