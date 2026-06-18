using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-009 加利·达丹（角色 / 炎 / 山贼，cost1 power2000）
/// 场上存在此卡牌以外的拥有《山贼》特征的角色的场合，此角色获得【双重攻击】效果。
///
/// 实现说明：
///   - 条件式持续赋予关键词，用 ContinuousEffect.GrantKeyword 注册（OnEnterField）。
///   - Predicate：仅授予自身（c.Id==selfId），且我方场上存在自身以外的《山贼》角色。
/// </summary>
public class OP13_009_Dadan : IScriptedEffect
{
    public string CardNumber => "OP13-009";

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
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId &&
                s.Players[owner].Characters.Any(ch => ch.Id != selfId && ch.Info.HasKeyword("山贼")),
        });

        return Task.CompletedTask;
    }
}
