using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-111 波尔·桑达索尼娅（角色，光，九蛇海盗团）
/// 【阻挡者】（关键词，由引擎处理）
/// 【触发】我方生命卡牌不多于 2 张的场合，此卡牌登场。
///
/// 实现说明 / 简化点：
///   - 【阻挡者】为纯关键词，由引擎处理，脚本不实现。
///   - 仅实现【触发】（生命牌触发）：生命触发发动时引擎已将本卡放入废弃区，
///     若我方生命牌数量 ≤2，则用 PlayFromTrashFree 从废弃区登场；否则本卡留在废弃区。
///   - 触发为强制（非"可以"），满足条件即登场。
/// </summary>
public class OP16_111_BoaSandersonia : IScriptedEffect
{
    public string CardNumber => "OP16-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：我方生命卡牌不多于 2 张
        if (me.LifeCount > 2) return Task.CompletedTask;

        // 此卡牌登场（发动触发时本卡已在废弃区）
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        return Task.CompletedTask;
    }
}
