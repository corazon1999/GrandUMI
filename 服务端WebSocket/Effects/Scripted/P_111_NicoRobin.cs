using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-111 妮古·罗宾（角色 / 风）
/// 【每回合1次】我方拥有《草帽一伙》特征的角色因对方的效果将要离开场上的场合，可以改为将我方的1张咚!!转为休息状态，使该角色不离场。
/// 成本：将我方1张活跃咚转为休息状态。
/// </summary>
public class P_111_NicoRobin : IScriptedEffect
{
    public string CardNumber => "P-111";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || !victim.Info.HasKeyword("草帽一伙")) return;
        var key = "P-111-leaveguard";
        if (me.TurnOnceUsed.Contains(key)) return;
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;                                    // 需有活跃咚作成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"妮古·罗宾：将我方1张咚!!转为休息状态，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);
        don.State = DonState.Rest;
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
