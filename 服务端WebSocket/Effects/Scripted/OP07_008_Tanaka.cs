using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-008 田中先生（角色 / 炎 / FILM・大德索罗号）
/// 【阻挡者】（纯关键词，由引擎处理，本脚本不实现）。
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌发动触发后已被引擎放入废弃区
///     (LifeRevealManager 先 Trash.Add 再 Resolve)，ctx.Source 即该卡。
///   - "此卡牌登场" = 从废弃区登场到我方场上，用 PlayFromTrashFree。
/// </summary>
public class OP07_008_Tanaka : IScriptedEffect
{
    public string CardNumber => "OP07-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Trash.Contains(self))
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        }

        return Task.CompletedTask;
    }
}
