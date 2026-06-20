using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-062 阿奈美（角色 / 暗 1 费 1000，草帽一伙）
/// 我方场上存在 10 张咚!! 的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件持续关键词，用 ContinuousEffect.GrantKeyword 注册（OnEnterField 注册，离场自动清理）。
///   - "场上存在 10 张咚!!" 以 TotalDonInCostArea 计（成本区咚!!总数，含活跃/休息/被赋予）。
/// </summary>
public class OP05_062_Anamu : IScriptedEffect
{
    public string CardNumber => "OP05-062";

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
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].TotalDonInCostArea >= 10,
        });

        return Task.CompletedTask;
    }
}
