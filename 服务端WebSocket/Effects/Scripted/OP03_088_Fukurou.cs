using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-088 猫头鹰（角色 / 地 / CP9，cost3 power3000）
/// 持续：此角色不会因效果而被KO。【阻挡者】（关键词，引擎处理）。
///
/// 实现说明：
///   - "不会因效果而被KO" 用 ContinuousEffect.KoGuard="effect" 注册（仅本卡自身），
///     来源卡离场时引擎自动清理。
///   - 【阻挡者】为关键词，由引擎统一处理，脚本不实现。
/// </summary>
public class OP03_088_Fukurou : IScriptedEffect
{
    public string CardNumber => "OP03-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.Id == selfId,
        });

        return Task.CompletedTask;
    }
}
