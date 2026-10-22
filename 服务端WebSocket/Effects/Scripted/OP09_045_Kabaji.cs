using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-045 卡巴奇（角色）
/// 我方场上存在角色"巴奇"或"毛奇"的场合，此角色在战斗中不会被KO。
/// 实现说明：条件性持续"战斗中不被KO"→ ContinuousEffect.KoGuard="battle"。
/// </summary>
public class OP09_045_Kabaji : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP09-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx) => Task.CompletedTask;

    public Task RegisterFieldStatic(EffectContext ctx)
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
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId &&
                s.Players[owner].Characters.Any(ch =>
                    ch.MatchesName("巴奇", s) || ch.MatchesName("毛奇", s)),
        });

        return Task.CompletedTask;
    }
}
