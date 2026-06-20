using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-106 萨罗门（角色 / 光）
/// 【阻挡者】
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 【触发】(OnLifeRevealTrigger)：此卡作为被翻开的生命牌已被引擎放入废弃区，
///     将其从废弃区登场到我方场上（参照 OP01-071 甚平写法）。
///   - 【阻挡者】为纯关键词，由引擎处理，此处不实现。
/// </summary>
public class OP14_106_Salome : IScriptedEffect
{
    public string CardNumber => "OP14-106";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
