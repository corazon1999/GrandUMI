using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-006 蒙奇·D·路飞（角色 / 炎 / 超新星・草帽一伙，cost3 power3000）
/// 【咚!!×2】【我方的回合中】此角色的力量+2000。
///
/// 实现：纯持续力量修正，用 ContinuousEffect.PowerDelta 注册（OnEnterField）。
///   条件：被赋予中的咚!! ≥2 且为我方回合中，自身 +2000。
/// </summary>
public class P_006_Luffy : IScriptedEffect
{
    public string CardNumber => "P-006";

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
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
