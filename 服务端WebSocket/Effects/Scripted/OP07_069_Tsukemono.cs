using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-069 酱菜（角色）
/// 我方场上咚!!的张数不多于对方场上咚!!的张数的场合，我方"酱菜"以外的拥有《福克斯海盗团》
/// 特征的角色不会因对方的效果而被 KO。
///   （条件性全体持续防KO，用 ContinuousEffect.KoGuard="effect" + Predicate 实现。）
/// </summary>
public class OP07_069_Tsukemono : IScriptedEffect
{
    public string CardNumber => "OP07-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        int opp = 1 - owner;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.Players[owner].TotalDonInCostArea <= s.Players[opp].TotalDonInCostArea &&
                !card.Info.NameContains("酱菜") &&
                card.Info.HasKeyword("福克斯海盗团"),
        });

        return Task.CompletedTask;
    }
}
