using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-033 小八（角色 / 水）
/// 【触发】我方领袖拥有《东海》特征的场合，此卡牌登场。
///
/// 实现：OnLifeRevealTrigger，仅当我方领袖含《东海》时，将此卡从废弃区登场。
/// </summary>
public class OP03_033_Hachi : IScriptedEffect
{
    public string CardNumber => "OP03-033";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("东海")) return Task.CompletedTask;
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
