using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-055 甚平（角色 / 光 / 鱼人族・草帽一伙，cost4 power5000）
/// 【触发】我方领袖拥有《鱼人族》或《人鱼族》特征且我方生命卡牌不多于 2 张的场合，此卡牌登场。
///
/// 实现说明（生命触发，OnLifeRevealTrigger）：
///   - 触发时此卡已在废弃区。条件满足（领袖含鱼人族或人鱼族 且 生命≤2）则将自身从废弃区登场。
/// </summary>
public class EB02_055_Jinbe : IScriptedEffect
{
    public string CardNumber => "EB02-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool leaderOk = me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族");
        if (leaderOk && me.LifeArea.Count <= 2)
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: false);
        return Task.CompletedTask;
    }
}
