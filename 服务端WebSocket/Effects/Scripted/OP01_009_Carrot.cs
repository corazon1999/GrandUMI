using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-009 凯罗特（角色）
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌，发动触发后已被引擎放入废弃区
///     (LifeRevealManager 先 p.Trash.Add(top) 再 Resolve)，ctx.Source 即该卡。
///   - "此卡牌登场" = 将其从废弃区登场到我方场上，用 PlayFromTrashFree。
/// </summary>
public class OP01_009_Carrot : IScriptedEffect
{
    public string CardNumber => "OP01-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅当此卡当前在废弃区(由生命触发流程放入)时，将其登场
        if (me.Trash.Contains(self))
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        }

        return Task.CompletedTask;
    }
}
