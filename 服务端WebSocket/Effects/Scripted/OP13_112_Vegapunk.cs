using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-112 贝加班克（角色 / 光 / 艾格赫德・科学家，cost1 power1000）
/// 我方合计存在2张或更多被赋予中的咚!!的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件式持续赋予关键词，用 ContinuousEffect.GrantKeyword 注册（OnEnterField）。
///   - 条件：我方 CostArea 中处于"被赋予中"(DonState.Attached)的咚合计≥2。
///   - Predicate 仅授予自身。
/// </summary>
public class OP13_112_Vegapunk : IScriptedEffect
{
    public string CardNumber => "OP13-112";

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
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId &&
                s.Players[owner].CostArea.Count(d => d.State == DonState.Attached) >= 2,
        });

        return Task.CompletedTask;
    }
}
