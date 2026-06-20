using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-074 匹卡（角色 / 暗）
/// 【每回合1次】此角色因对方的效果将要被KO的场合，可以改为将我方2张活跃状态的咚!!转为休息状态，使此角色不会被KO。
/// 实现：OnAllyWillLeaveField 自我置换，仅 kind=="ko"；每回合1次；横置2张活跃咚作成本后 MarkPreventLeave。
/// </summary>
public class OP10_074_Pica : IScriptedEffect
{
    public string CardNumber => "OP10-074";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var kind = ctx.Vars.TryGetValue("kind", out var kv) ? kv as string : null;
        if (vId != self.Id.ToString() || kind != "ko") return;      // 仅自身、仅因效果被KO
        var key = "OP10-074-leaveguard" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        var actives = me.CostArea.Where(d => d.State == DonState.Active).Take(2).ToList();
        if (actives.Count < 2) return;                              // 需2张活跃咚

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "匹卡：将我方2张活跃咚转为休息状态，使此角色不会被KO？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);
        foreach (var d in actives) d.State = DonState.Rest;
        ctx.State.MarkPreventLeave(self.Id);
    }
}
