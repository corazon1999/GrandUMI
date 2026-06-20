using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-079 维尔高（角色 / 地 / 海军·堂吉诃德海盗团）
/// 【咚!!×1】此角色在战斗中不会被KO。
///
/// 实现说明：
///   - 持续"不会被KO（仅战斗中）"，且条件为本卡被赋予中的咚!! ≥1（【咚!!×1】）。
///   - 用 ContinuousEffect.KoGuard="battle" 注册，Predicate 限定本卡且其附着咚 ≥1。
///   - 在 OnEnterField 注册，离场由引擎自动清理；重复登场前先去重。
/// </summary>
public class OP03_079_Vergo : IScriptedEffect
{
    public string CardNumber => "OP03-079";

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
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                sideIdx == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
