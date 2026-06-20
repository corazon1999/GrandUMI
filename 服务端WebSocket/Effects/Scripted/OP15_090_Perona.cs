using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-090 佩罗娜（角色 / 地）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为丢弃我方的1张手牌，使该角色不离场。
/// </summary>
public class OP15_090_Perona : IScriptedEffect
{
    public string CardNumber => "OP15-090";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"佩罗娜：丢弃我方1张手牌，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (ch.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == ch[0]);
        if (card is null) return;
        AtomicOps.DiscardHand(me, card);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
