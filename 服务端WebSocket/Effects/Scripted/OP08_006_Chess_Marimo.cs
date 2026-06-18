using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-006 棋子莫里莫（角色）
/// 【我方的回合中】我方废弃区中存在"黑莫里莫"和"棋子"的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 在登场时注册。
///   - Predicate：仅本角色自身、且为我方回合、且我方废弃区同时存在名为"黑莫里莫"和"棋子"的卡牌时生效。
///   - 来源卡离场时由引擎自动清理；重复登场前先去重。
/// </summary>
public class OP08_006_Chess_Marimo : IScriptedEffect
{
    public string CardNumber => "OP08-006";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].Trash.Any(c => c.MatchesName("黑莫里莫")) &&
                s.Players[owner].Trash.Any(c => c.MatchesName("棋子")),
        });

        return Task.CompletedTask;
    }
}
