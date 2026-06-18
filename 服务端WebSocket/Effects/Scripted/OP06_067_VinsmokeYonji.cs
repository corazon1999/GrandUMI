using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-067 温思默克·勇智（角色 / 暗 4 费 5000 / 温思默克家·GERMA 66）
/// 我方场上咚!!的张数不多于对方场上咚!!的张数的场合，此角色的力量+1000。
/// 【阻挡者】（常驻关键词）。
///
/// 实现：
///  - 注册条件性 PowerDelta=+1000 的持续效果（我方咚数 ≤ 对方咚数时），谓词限定本卡自身。
///  - 注册无条件 GrantKeyword="阻挡者"（始终生效）。
/// 「场上咚!!张数」取 TotalDonInCostArea（费用区内全部咚卡，含活跃/休息/被赋予）。
/// </summary>
public class OP06_067_VinsmokeYonji : IScriptedEffect
{
    public string CardNumber => "OP06-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 条件性力量 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].TotalDonInCostArea <= s.Players[1 - owner].TotalDonInCostArea,
        });

        // 常驻【阻挡者】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.Id == selfId,
        });

        return Task.CompletedTask;
    }
}
