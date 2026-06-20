using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-082 莫奈（角色 / 水 / 堂吉诃德海盗团·班克禁区）
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌已被引擎放入废弃区，
///     "此卡牌登场" = 将其从废弃区登场到我方场上，用 PlayFromTrashFree。
/// </summary>
public class OP01_082_Monet : IScriptedEffect
{
    public string CardNumber => "OP01-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
