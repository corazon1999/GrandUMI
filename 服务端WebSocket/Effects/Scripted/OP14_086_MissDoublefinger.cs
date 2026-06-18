using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-086 Miss.双指(萨拉)（角色 / 地）
/// 我方废弃区中有 7 张或更多卡牌的场合，此角色的力量 +1000，
/// 且我方所有拥有〈巴洛克工作室〉特征的角色费用 +2。
///
/// 实现说明：
///   - 两条持续效果（登场时注册）：
///     1) PowerDelta=+1000，限定本卡自身，废弃≥7 时生效。
///     2) CostDelta=+2，限定我方〈巴洛克工作室〉角色，废弃≥7 时生效。
///   - 废弃区张数用 s.Players[owner].Trash.Count 判断。
/// </summary>
public class OP14_086_MissDoublefinger : IScriptedEffect
{
    public string CardNumber => "OP14-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 1) 废弃≥7 时此角色力量 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && sideIdx == owner &&
                s.Players[owner].Trash.Count >= 7,
        });

        // 2) 废弃≥7 时我方〈巴洛克工作室〉角色费用 +2
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("巴洛克工作室"),
            },
            CostDelta = 2,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && s.Players[owner].Trash.Count >= 7,
        });

        return Task.CompletedTask;
    }
}
