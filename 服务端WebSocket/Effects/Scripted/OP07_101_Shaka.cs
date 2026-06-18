using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-101 释迦（角色）
/// 【阻挡者】（纯关键词，由引擎处理，不在此实现）
/// 【触发】我方领袖为"贝加班克"的场合，此卡牌登场。
///
/// 实现说明：
///   - 【触发】(OnLifeRevealTrigger)：生命牌触发时此卡已被放入废弃区，
///     仅当我方领袖名为"贝加班克"时从废弃区免费登场；否则留在废弃区。
/// </summary>
public class OP07_101_Shaka : IScriptedEffect
{
    public string CardNumber => "OP07-101";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖为"贝加班克"
        if (!me.Leader.Info.NameContains("贝加班克")) return Task.CompletedTask;

        // 此卡已在废弃区，从废弃区免费登场（活跃状态）
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: false);
        return Task.CompletedTask;
    }
}
