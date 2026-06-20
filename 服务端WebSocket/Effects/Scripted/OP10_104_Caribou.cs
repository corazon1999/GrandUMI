using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-104 卡里布（角色 / 光 4 费 5000，超新星·卡里布海盗团）
/// 【咚!!×1】我方领袖拥有《超新星》特征且对方生命卡牌为3张或更多的场合，此角色在战斗中不会被KO。
///
/// 实现：条件持续 KoGuard="battle"（规范 13.2），在 OnEnterField 注册。
///   Predicate：本卡自身、被赋予中的咚!! ≥1、我方领袖含《超新星》、对方生命 ≥3 时生效。
/// </summary>
public class OP10_104_Caribou : IScriptedEffect
{
    public string CardNumber => "OP10-104";

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
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].Leader.Info.HasKeyword("超新星") &&
                s.Players[1 - owner].LifeCount >= 3,
        });
        return Task.CompletedTask;
    }
}
