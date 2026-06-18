using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-005 斯摩格（角色 / 炎 / 海军）
/// 【阻挡者】（引擎关键词，自动处理）
/// 【咚!!×1】此角色不会因没有属性（特）的角色的效果而被KO。
///
/// 实现说明 / 简化点：
///   - OnEnterField 注册条件性 KoGuard="effect"（仅因效果被KO时保护），谓词限定本卡且其上贴有 ≥1 张咚!!。
///   - KoGuard 谓词只能看到被保护卡本身，无法获知造成KO的来源角色的属性，故"没有属性（特）的角色的效果"
///     这一来源限定无法精确判定，简化为：贴有 ≥1 咚时不因任何效果被KO（覆盖卡面主要场景）。
/// </summary>
public class OP11_005_Smoker : IScriptedEffect
{
    public string CardNumber => "OP11-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
