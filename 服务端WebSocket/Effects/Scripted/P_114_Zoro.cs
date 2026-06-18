using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-114 罗罗诺亚·佐罗（角色 / 风）
/// 【阻挡者】（关键词，由引擎处理）。
/// 【我方的回合结束时】我方场上存在处于活跃状态的咚!!的场合，将此角色转为活跃状态。
///
/// 实现说明：
///   - 仅实现主动效果部分；【阻挡者】为纯关键词，引擎自动处理。
///   - "存在活跃咚"用 me.ActiveDonCount > 0 判定（PlayerState.ActiveDonCount =
///     CostArea 中 State==Active 的咚数）。满足则 ActivateCard(self) 转为活跃。
/// </summary>
public class P_114_Zoro : IScriptedEffect
{
    public string CardNumber => "P-114";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.ActiveDonCount > 0)
        {
            AtomicOps.ActivateCard(ctx.Source);
        }
        return Task.CompletedTask;
    }
}
