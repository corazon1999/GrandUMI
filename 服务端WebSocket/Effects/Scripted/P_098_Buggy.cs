using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-098 巴奇（角色 / 水）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】我方场上不存在5张费用为5或更高的角色的场合，将此角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 仅实现【登场时】主动效果；【阻挡者】为关键词由引擎处理。
///   - 条件「我方场上不存在5张费用≥5的角色」= 我方场上费用≥5的角色数量 < 5 时成立，
///     此时将自身放回卡组最下方（ReturnFieldToDeckBottom）。
/// </summary>
public class P_098_Buggy : IScriptedEffect
{
    public string CardNumber => "P-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int costGe5 = me.Characters.Count(c => c.Info.Cost >= 5);
        if (costGe5 < 5)
        {
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);
        }
        return Task.CompletedTask;
    }
}
