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
        // 启动主要每回合 1 次：打上本回合标记即可。"本回合内发动原始费用≥3 事件时抽 1"的实际联动
        // 在 GameEngine.ResolveEffectAsync 事件分支读取此 TurnOnceUsed 标记完成（出牌处才拿得到事件费用）。
        me.TurnOnceUsed.Add(key);
        await Task.CompletedTask;
    }
}
