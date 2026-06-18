using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-032 达斯琪（角色 / 风）
/// 我方"达斯琪"以外的绿色角色因对方的效果将要离开场上的场合，可以改为将此角色转为休息状态，使该角色不离场。
/// 成本：将此角色(达斯琪自身)转为休息状态。
/// </summary>
public class OP10_032_Tashigi : IScriptedEffect
{
    public string CardNumber => "OP10-032";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        if (vId == self.Id.ToString()) return;                      // 达斯琪以外
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null) return;
        if (!victim.Info.ColorList.Contains("绿")) return;          // 绿色角色
        if (self.IsTapped) return;                                  // 成本需自身活跃

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"达斯琪：将此角色转为休息状态，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        AtomicOps.RestCard(self);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
