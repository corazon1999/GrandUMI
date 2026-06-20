using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-003 闪电（角色 / 炎 3 费 4000，革命军）
/// 我方场上存在此角色以外的力量 7000 或更高的角色的场合，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 条件持续关键词，用 ContinuousEffect.GrantKeyword 注册（OnEnterField 注册，离场自动清理）。
///   - "力量 7000 或更高的角色" 取当前力量 s.CurrentPowerOf（含持续效果）；
///     遍历我方领袖与角色，排除本卡自身。
/// </summary>
public class OP05_003_Shanks : IScriptedEffect
{
    public string CardNumber => "OP05-003";

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
            {
                if (sideIdx != owner || c.Id != selfId) return false;
                var p = s.Players[owner];
                bool leaderOk = s.CurrentPowerOf(owner, p.Leader) >= 7000;
                bool charOk = p.Characters.Any(ch => ch.Id != selfId && s.CurrentPowerOf(owner, ch) >= 7000);
                return leaderOk || charOk;
            },
        });

        return Task.CompletedTask;
    }
}
