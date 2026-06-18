using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-005 特拉法尔加·罗（角色 / 炎 / 王下七武海・超新星・心脏海盗团，cost3 power5000）
/// 对方场上不存在 2 张或更多原本的力量为 5000 或更高的角色的场合，此角色无法攻击。
///
/// 实现说明（M1 条件静态禁攻）：
///   - 用 ContinuousEffect.GrantRestriction = CannotAttack 注册（OnEnterField），
///     由 ActionValidator.CanAttack 每次实时查询（s.HasContinuousRestriction）。
///   - HasContinuousRestriction 不校验 Scope，故谓词内显式限定 c.Id == selfId（仅作用本卡）。
///   - 谓词成立 = 限制生效 = 对方力量≥5000的角色不足 2 张 → 此角色无法攻击。
///   - "原本的力量"按卡面印刷力量 Info.Power 判定。
/// </summary>
public class EB04_005_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "EB04-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantRestriction = RestrictionKind.CannotAttack,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[1 - sideIdx].Characters.Count(x => x.Info.Power >= 5000) < 2,
        });

        return Task.CompletedTask;
    }
}
