using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-069 诺拉（角色 / 暗）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为将我方场上的1张咚!!放回咚!!卡组，使该角色不离场。
/// </summary>
public class OP15_069_Nora : IScriptedEffect
{
    public string CardNumber => "OP15-069";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        if (me.CostArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"诺拉：将我方1张咚!!放回咚卡组，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
