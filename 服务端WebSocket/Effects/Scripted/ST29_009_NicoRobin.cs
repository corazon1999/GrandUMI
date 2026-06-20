using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST29-009 妮古·罗宾（角色）
/// 关键词【阻挡者】由引擎处理，无需脚本。
/// 【触发】我方领袖为"蒙奇·D·路飞"的场合，此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌，发动触发前已被引擎放入废弃区
///     (LifeRevealManager 先 p.Trash.Add(top) 再 Resolve)，ctx.Source 即该卡。
///   - 仅当我方领袖名为"蒙奇·D·路飞"时，将其从废弃区登场，用 PlayFromTrashFree。
///     （参照 OP01-009 凯罗特，附加领袖名条件）
/// </summary>
public class ST29_009_NicoRobin : IScriptedEffect
{
    public string CardNumber => "ST29-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：我方领袖为"蒙奇·D·路飞"
        if (!me.Leader.Info.NameIs("蒙奇·D·路飞")) return Task.CompletedTask;

        // 仅当此卡当前在废弃区(由生命触发流程放入)时，将其登场
        if (me.Trash.Contains(self))
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
        }

        return Task.CompletedTask;
    }
}
