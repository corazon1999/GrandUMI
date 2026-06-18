using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-088 特拉法尔加·罗（角色 / 光）
/// 【触发】我方领袖拥有《超新星》特征，且双方生命卡牌合计张数不多于5张的场合，此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：本卡作为被翻开的生命牌，发动触发时引擎已将其放入废弃区
///     (LifeRevealManager 先 Trash.Add 再 Resolve)，ctx.Source 即本卡。
///   - 条件满足时用 PlayFromTrashFree 将其从废弃区登场；不满足则保持在废弃区。
/// </summary>
public class P_088_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "P-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：领袖《超新星》且双方生命合计 ≤ 5
        if (!me.Leader.Info.HasKeyword("超新星")) return Task.CompletedTask;
        if (me.LifeCount + opp.LifeCount > 5) return Task.CompletedTask;

        if (me.Trash.Contains(self))
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        }
        return Task.CompletedTask;
    }
}
