using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-049 萨波（角色 / 水）
/// 我方"萨波"以外的原本的费用不高于7的角色因对方的效果将要离开场上的场合，可以改为将此角色放回其持有者的手牌，使该角色不离场。
/// 成本：将此角色(萨波自身)放回手牌。
/// </summary>
public class OP10_049_Sabo : IScriptedEffect
{
    public string CardNumber => "OP10-049";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        if (vId == self.Id.ToString()) return;                      // 萨波以外
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Cost > 7) return;         // 原本费用≤7

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"萨波：将此角色放回手牌，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        ctx.State.MarkPreventLeave(victim.Id);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, self);    // 成本：萨波自身回手牌
    }
}
