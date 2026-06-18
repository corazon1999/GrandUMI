using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-030 帕帕格（角色）
/// 我方场上存在角色"凯米"的场合，此角色获得【阻挡者】效果。
///   （条件性持续赋予关键词，用 ContinuousEffect.GrantKeyword + Predicate 实现。）
/// </summary>
public class OP07_030_Pappag : IScriptedEffect
{
    public string CardNumber => "OP07-030";

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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                sideIdx == owner &&
                s.Players[owner].Characters.Any(c => c.Info.NameContains("凯米")),
        });

        return Task.CompletedTask;
    }
}
