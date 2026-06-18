using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-045 加尔根（角色）
/// 【阻挡者】（由引擎关键词处理）
/// 【对方的回合中】我方卡组不多于20张的场合，此角色的力量+3000。
///
/// 实现说明：纯持续条件力量修正 → ContinuousEffect.PowerDelta=3000，
///   Predicate 限自身、对方回合中、且我方卡组张数≤20。
/// </summary>
public class OP03_045_Gargon : IScriptedEffect
{
    public string CardNumber => "OP03-045";

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
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Deck.Count <= 20,
        });
        return Task.CompletedTask;
    }
}
