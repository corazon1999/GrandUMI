using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-045 沙奇（角色 / 水）
/// 此角色将要被KO的场合，或因对方效果将要离开场上的场合，改为将此角色放置到废弃区，并抽取1张卡牌，以代替被KO或离场。
/// 实现：OnAllyWillLeaveField 自我置换（victim==自身，强制）；取消原本离场，改为将自身放入废弃区并抽1张。
/// </summary>
public class OP08_045_Shachi : IScriptedEffect
{
    public string CardNumber => "OP08-045";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        if (vId != self.Id.ToString()) return Task.CompletedTask;   // 仅自身

        // 取消原本离场，改为：自身放入废弃区 + 抽1张
        ctx.State.MarkPreventLeave(self.Id);
        foreach (var d in me.CostArea)
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            { d.State = DonState.Rest; d.AttachedToCardId = null; }
        if (me.Characters.Remove(self)) me.Trash.Add(self);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
