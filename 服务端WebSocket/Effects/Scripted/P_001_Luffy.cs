using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-001 蒙奇·D·路飞（角色 / 炎 / 超新星・草帽一伙，cost6 power7000）
/// 【咚!!×2】此角色获得【速攻】效果。
///
/// 实现：被赋予中的咚!! ≥2 时，自身获得【速攻】，用 ContinuousEffect.GrantKeyword 注册（OnEnterField）。
/// </summary>
public class P_001_Luffy : IScriptedEffect
{
    public string CardNumber => "P-001";

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
            GrantKeyword = "速攻",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
