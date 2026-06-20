using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST05-009 斯嘉丽（角色 / 暗 2 费 3000，FILM/动物/金狮子海盗团）
/// 【触发】此卡牌登场。
///
/// 实现说明：
/// - 【触发】= 生命牌触发 OnLifeRevealTrigger。发动触发时，LifeRevealManager 已将该生命牌
///   从生命区移除并放入废弃区（见 LifeRevealManager.DealDamageToLeader）。
/// - 因此 ctx.Source（即此卡）此刻位于废弃区，用 PlayFromTrashFree 把它从废弃区登场到角色区，
///   即可表达"此卡牌登场"（不加入手牌而是直接登场）。
/// </summary>
public class ST05_009_Scarlet : IScriptedEffect
{
    public string CardNumber => "ST05-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        // 触发时此卡已在废弃区，从废弃区登场到角色区。
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
