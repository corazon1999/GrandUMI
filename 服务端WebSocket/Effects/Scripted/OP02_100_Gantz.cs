using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-100 强高（角色）
/// 我方场上存在"赫波迪"的场合，此角色不会在战斗中被KO。
///
/// 实现说明：
///   - 持续"战斗中不被KO"用 ContinuousEffect.KoGuard="battle"。
///   - Predicate：当我方场上存在名为"赫波迪"的角色时，对本卡生效。
///   - 在 OnEnterField 注册，离场由引擎自动清理；RemoveAll 去重。
/// </summary>
public class OP02_100_Gantz : IScriptedEffect
{
    public string CardNumber => "OP02-100";

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
                s.Players[owner].Characters.Any(c => c.MatchesName("赫波迪")),
        });

        return Task.CompletedTask;
    }
}
