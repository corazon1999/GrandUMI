using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-091 烬（领航 / 暗 / 百兽海盗团，cost5 power5000）
/// 【我方的回合中】我方场上存在10张咚!!的场合，对方的所有角色力量-1000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 注册（领袖在 OnGameStart 注册一次）。
///   - Scope.Side=1（对方），仅角色；Predicate：我方回合中且我方费用区合计咚!!达10张。
/// </summary>
public class OP01_091_Jin : IScriptedEffect
{
    public string CardNumber => "OP01-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -1000,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].TotalDonInCostArea >= 10,
        });
        return Task.CompletedTask;
    }
}
