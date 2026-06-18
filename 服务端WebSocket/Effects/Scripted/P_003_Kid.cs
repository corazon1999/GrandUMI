using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-003 尤斯塔斯·基德（角色 / 风 / 超新星・基德海盗团，cost3 power4000）
/// 【咚!!×2】此角色获得【双重攻击】效果。
///
/// 实现：被赋予中的咚!! ≥2 时，自身获得【双重攻击】，用 ContinuousEffect.GrantKeyword 注册（OnEnterField）。
/// </summary>
public class P_003_Kid : IScriptedEffect
{
    public string CardNumber => "P-003";

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
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
