using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST03-013 波尔·汉库珂（角色 / 水 / 王下七武海・九蛇海盗团）
/// effectText：仅【阻挡者】纯关键词（由引擎处理，无主动效果）。
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 生命触发(OnLifeRevealTrigger)：此卡作为被翻开的生命牌已被引擎放入废弃区，
///     将其从废弃区登场到我方场上（参照 OP04-113 拉比安 / OP01-071 甚平同款写法）。
///   - 【阻挡者】为纯关键词，由引擎自动处理，脚本只需实现触发自登场部分。
/// </summary>
public class ST03_013_BoaHancock : IScriptedEffect
{
    public string CardNumber => "ST03-013";

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
