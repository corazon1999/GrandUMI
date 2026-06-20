using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-113 拉比安（角色 / 光 / 大妈海盗团・Homies）
/// 效果文本：无。
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌已被引擎放入废弃区，
///     将其从废弃区登场到我方场上（参照 OP01-071 甚平同款写法）。
/// </summary>
public class OP04_113_Rabian : IScriptedEffect
{
    public string CardNumber => "OP04-113";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 此卡牌登场：从废弃区登场到我方场上
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
