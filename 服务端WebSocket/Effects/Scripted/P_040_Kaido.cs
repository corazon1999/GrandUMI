using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-040 盖德（角色）
/// 对方场上存在 10 张咚!! 的场合，此角色不会被 KO。
///   → 持续防 KO，用 ContinuousEffect.KoGuard="any" 注册（对方咚卡区合计=10 时生效）。
/// </summary>
public class P_040_Kaido : IScriptedEffect
{
    public string CardNumber => "P-040";

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
            KoGuard = "any",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[1 - owner].TotalDonInCostArea >= 10,
        });
        return Task.CompletedTask;
    }
}
