using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-118 奈菲特·薇薇（角色 / 炎 / 阿拉巴斯坦王国）
/// 此角色以外的我方所有费用为 3 或更高的红色（炎）角色获得【速攻】。
///
/// 实现说明：
///   - 纯持续/静态修正，用 ContinuousEffect.GrantKeyword="速攻" 注册（OnEnterField 时机）。
///   - Predicate：源卡同方、为角色、非自身、含"红"色、当前费用≥3。
/// </summary>
public class OP04_118_NefeltariVivi : IScriptedEffect
{
    public string CardNumber => "OP04-118";

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
                sideIdx == owner &&
                c.Info.Kind == CardKind.Character &&
                c.Id != selfId &&
                c.Info.ColorList.Contains("红") &&
                s.CurrentCostOf(sideIdx, c) >= 3,
        });
        return Task.CompletedTask;
    }
}
