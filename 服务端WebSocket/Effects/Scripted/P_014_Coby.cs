using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-014 可比（角色 / 炎 / FILM・海军，cost3 power3000）
/// 【阻挡者】（关键词，由引擎处理）
/// 【触发】此卡牌登场。
///
/// 实现：生命触发（OnLifeRevealTrigger）将此卡从废弃区/生命区登场，无附加成本。
/// </summary>
public class P_014_Coby : IScriptedEffect
{
    public string CardNumber => "P-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
