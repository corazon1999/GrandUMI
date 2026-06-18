using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST08-007 奈菲特·薇薇（角色）
/// 关键词【阻挡者】由引擎处理。
/// 【触发】此卡牌登场。
///   生命牌受到伤害揭开并选择发动触发时，引擎已将本卡移入废弃区，
///   故"此卡牌登场"实现为从废弃区免费登场（PlayFromTrashFree）。
/// </summary>
public class ST08_007_NefertariVivi : IScriptedEffect
{
    public string CardNumber => "ST08-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        // 触发发动时本卡已位于废弃区，将其免费登场到场上
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
