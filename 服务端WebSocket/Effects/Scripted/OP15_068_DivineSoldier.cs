using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-068 神兵（角色）
/// 持续：我方场上咚!!的张数不多于 6 张的场合，此角色获得【阻挡者】。
///   （ContinuousEffect.GrantKeyword 注册，Predicate 限定到本卡且按条件成立。）
/// </summary>
public class OP15_068_DivineSoldier : IScriptedEffect
{
    public string CardNumber => "OP15-068";

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
                s.Players[owner].TotalDonInCostArea <= 6,
        });

        return Task.CompletedTask;
    }
}
