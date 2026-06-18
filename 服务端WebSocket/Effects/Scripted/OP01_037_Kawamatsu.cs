using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-037 河松（角色 / 风 / 鱼人族·和之国·赤鞘九人男）
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌，发动触发后已被引擎放入废弃区，
///     ctx.Source 即该卡。"此卡牌登场" = 将其从废弃区登场到我方场上，用 PlayFromTrashFree。
///   - 与 OP01-009 凯罗特同模式。
/// </summary>
public class OP01_037_Kawamatsu : IScriptedEffect
{
    public string CardNumber => "OP01-037";

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
