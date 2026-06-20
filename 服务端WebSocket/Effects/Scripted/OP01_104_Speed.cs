using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-104 斯皮德（角色 / 暗）
/// 【触发】此卡牌登场。
///
/// 实现：OnLifeRevealTrigger 时机，此卡已被 LifeRevealManager 放入废弃区，ctx.Source 即该卡，
/// 用 PlayFromTrashFree 将其从废弃区登场（同 OP01-009 凯罗特惯例）。
/// </summary>
public class OP01_104_Speed : IScriptedEffect
{
    public string CardNumber => "OP01-104";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
