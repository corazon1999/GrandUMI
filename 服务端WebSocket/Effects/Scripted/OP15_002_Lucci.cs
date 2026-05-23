using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-002 路西（领航）
/// 攻击/被攻击时：可丢弃任意张事件/舞台，每张本次战斗本领袖力量 +1000
/// 启动主要每回合 1 次：本回合内发动费用 ≥3 事件时抽 1
/// 当前实现：仅启动主要部分（攻击时的"按张数加力量"需 BattleEngine 扩展）
/// </summary>
public class OP15_002_Lucci : IScriptedEffect
{
    public string CardNumber => "OP15-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        const string key = "OP15-002-MainOncePerTurn";
        if (me.TurnOnceUsed.Contains(key)) return;
        // 该效果是"本回合内 ≥3 费事件抽 1"，需在 PlayCard 时联动；
        // 此处简化：直接给"本回合"标记，PlayCard 处可读取标记额外抽
        ctx.Vars["LucciEventDrawArmed"] = true;
        me.TurnOnceUsed.Add(key);
        await Task.CompletedTask;
    }
}
