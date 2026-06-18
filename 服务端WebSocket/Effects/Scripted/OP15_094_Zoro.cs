using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-094 罗罗诺亚·佐罗（角色 / 地）
/// 我方此角色以外的拥有《草帽一伙》特征的角色因对方的效果将要离开场上的场合，可以改为将此角色放置到废弃区，使该角色不离场。【阻挡者】
/// 成本：将此角色(佐罗自身)放入废弃区。
/// </summary>
public class OP15_094_Zoro : IScriptedEffect
{
    public string CardNumber => "OP15-094";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        if (vId == self.Id.ToString()) return;                      // 此角色以外
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || !victim.Info.HasKeyword("草帽一伙")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"佐罗：将此角色放入废弃区，使「{victim.Info.Name}」不离场？");
        if (!use) return;

        ctx.State.MarkPreventLeave(victim.Id);
        // 成本：佐罗自身放入废弃区（归还附着咚）
        foreach (var d in me.CostArea)
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            { d.State = DonState.Rest; d.AttachedToCardId = null; }
        if (me.Characters.Remove(self)) me.Trash.Add(self);
    }
}
