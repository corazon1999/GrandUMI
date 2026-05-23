using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-058 艾尼路（领航）
/// 持续规则：咚卡组变为 6 张（需引擎初始化时识别）
/// 启动主要每回合 1 次（第 2 回合起）：追加 1 活跃 + 最多 4 休息咚
/// </summary>
public class OP15_058_Enel : IScriptedEffect
{
    public string CardNumber => "OP15-058";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        const string key = "OP15-058-MainOncePerTurn";
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        if (ctx.State.TurnCount < 2) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        AtomicOps.RefreshDonFromDeck(me, 4, DonState.Rest);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
