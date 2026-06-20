using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-072 柯赛特（角色 / 暗 1 费 0 / GERMA王国）
/// 我方领袖拥有《GERMA 66》特征，且我方场上咚!!的张数比对方场上咚!!的张数少 2 张或更多的场合，
/// 此角色获得【阻挡者】效果。
///
/// 实现：注册条件性 GrantKeyword="阻挡者" 的持续效果，谓词限定本卡自身，
/// 条件为我方领袖含《GERMA 66》且 我方咚数 ≤ 对方咚数-2。
/// 「场上咚!!张数」取 TotalDonInCostArea。
/// </summary>
public class OP06_072_Cosette : IScriptedEffect
{
    public string CardNumber => "OP06-072";

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
                s.Players[owner].Leader.Info.HasKeyword("GERMA 66") &&
                s.Players[owner].TotalDonInCostArea <= s.Players[1 - owner].TotalDonInCostArea - 2,
        });

        return Task.CompletedTask;
    }
}
