using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-074 侯斯（角色 / 暗 / 因佩尔地狱，cost1 power2000）
/// 我方的"蓝猩"获得【阻挡者】效果。
///
/// 实现说明：
///   - 持续赋予关键词用 ContinuousEffect.GrantKeyword（Wave1）。
///   - Predicate 限定我方一侧、卡名包含"蓝猩"的角色。来源卡（本卡）离场时引擎自动清理。
/// </summary>
public class OP02_074_Hannyabal : IScriptedEffect
{
    public string CardNumber => "OP02-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.MatchesName("蓝猩"),
        });

        return Task.CompletedTask;
    }
}
