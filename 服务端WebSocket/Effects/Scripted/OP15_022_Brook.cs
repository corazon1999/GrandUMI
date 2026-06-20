using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-022 布鲁克（领航）
/// 启动主要每回合 1 次：将卡组顶 4 张放废弃区
/// 持续规则：卡组 0 张不立即败北，但变为 0 的回合结束时败北（特殊判定，需引擎扩展）
/// </summary>
public class OP15_022_Brook : IScriptedEffect
{
    public string CardNumber => "OP15-022";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP15-022-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        AtomicOps.MillTop(me, 4);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
