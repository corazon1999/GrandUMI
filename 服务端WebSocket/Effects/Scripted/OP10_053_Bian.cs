using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-053 比安（角色 / 水 1 费 1000，东踏踏族·德莱斯罗兹）
/// 我方场上存在"比安"以外的拥有《东踏踏族》特征的角色的场合，此角色获得【阻挡者】效果。
///
/// 实现：条件持续 GrantKeyword="阻挡者"（规范 13.1），在 OnEnterField 注册。
///   Predicate 限定为本卡自身，且条件为我方场上存在另一张《东踏踏族》角色（排除自身）。
/// </summary>
public class OP10_053_Bian : IScriptedEffect
{
    public string CardNumber => "OP10-053";

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
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].Characters.Any(c => c.Id != selfId && c.Info.HasKeyword("东踏踏族")),
        });
        return Task.CompletedTask;
    }
}
