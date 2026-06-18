using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-070 弗兰介（角色 / 暗 5 费 4000，草帽一伙）
/// 【咚!!×1】我方场上存在 8 张或更多咚!! 的场合，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 【咚!!×1】的赋予咚成本由引擎处理；此处实现条件持续关键词收益。
///   - 用 ContinuousEffect.GrantKeyword 注册（OnEnterField 注册，离场自动清理），
///     谓词同时要求本卡被赋予的咚!! ≥1 且我方场上咚!!总数 ≥8。
/// </summary>
public class OP05_070_Franky : IScriptedEffect
{
    public string CardNumber => "OP05-070";

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
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].TotalDonInCostArea >= 8,
        });

        return Task.CompletedTask;
    }
}
