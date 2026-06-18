using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-005 伪草帽一伙（角色 / 炎）
/// 【我方的回合中】此角色的力量 +2000。【对方的回合中】此角色的力量 -2000。
///
/// 实现说明：
///   - 纯持续/静态力量修正，用两条 ContinuousEffect 注册（OnEnterField 时机），仅作用于自身。
///   - 我方回合 +2000、对方回合 -2000；离场后引擎自动清理。
/// </summary>
public class EB02_005_FakeStrawHat : IScriptedEffect
{
    public string CardNumber => "EB02-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 【我方的回合中】此角色 +2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer == owner,
        });

        // 【对方的回合中】此角色 -2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -2000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
