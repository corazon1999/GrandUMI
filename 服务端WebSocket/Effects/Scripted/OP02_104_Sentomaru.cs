using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-104 战桃丸（角色 / 风）
/// 【触发】此卡牌登场。
///
/// 实现：OnLifeRevealTrigger 时此卡已在废弃区，用 PlayFromTrashFree 将其从废弃区登场（同 OP01-104 惯例）。
/// </summary>
public class OP02_104_Sentomaru : IScriptedEffect
{
    public string CardNumber => "OP02-104";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
