using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-009 可比（角色 / 炎）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为本回合中我方的领袖力量-2000，使该角色不离场。
/// 实现：OnAllyWillLeaveField 守护；victim 为我方原力≤7000角色时，可选成本（本回合领袖-2000）后 MarkPreventLeave。
/// </summary>
public class OP15_009_Koby : IScriptedEffect
{
    public string CardNumber => "OP15-009";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"可比：本回合我方领袖力量-2000，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        AtomicOps.AddPowerThisTurn(me.Leader, -2000);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
