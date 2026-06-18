using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-117 破坏之弦（事件）
/// 【反击】本次战斗中，我方领袖力量+3000。
///   —— 直接对我方领袖施加本次战斗力量+3000。
///
/// 简化点（部分效果判 complex，本脚本只实现【反击】部分）：
///   - 文本另含【主要】将费用≤9的角色"正面朝下加入其持有者生命区的最上方或最下方"，
///     涉及位置选择（最上/最下）与放入持有者生命区的机制，引擎 MoveCharToLife 无法精确表达，
///     该主要效果未实现。
///   - 本脚本仅实现可表达的【反击】：本次战斗我方领袖+3000。
/// </summary>
public class OP12_117_DestructionString : IScriptedEffect
{
    public string CardNumber => "OP12-117";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        AtomicOps.AddPowerThisBattle(me.Leader, 3000);
        return Task.CompletedTask;
    }
}
