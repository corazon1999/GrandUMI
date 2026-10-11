using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-092 可比（角色 / 炎）
/// 【对方回合中】此角色的力量-3000。
/// 【攻击时】我方领袖拥有《海军》特征的场合，直到下个对方的回合结束为止，
///   我方领袖原本的力量变为7000。
///
/// 实现说明：
///   - 【对方回合中】-3000：通过独立静态场上能力注册，仅在对方回合作用于自身。
///   - 【攻击时】在领袖实例上记录原本力量覆盖，直到下个对方回合结束；克比离场不终止已结算效果。
/// </summary>
public class P_092_Koby : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "P-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString() + "-self");
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString() + "-self",
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -3000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
        });
        return Task.CompletedTask;
    }

    public Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare) return Task.CompletedTask;
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        // OnAttackDeclare：领袖《海军》时，领袖原本力量变为7000，直到下个对方回合结束
        if (!me.Leader.Info.HasKeyword("海军")) return Task.CompletedTask;

        // 已结算的限时效果属于领袖，不依赖克比继续留场；原本力量覆盖也不会重复相加。
        AtomicOps.SetOriginalPowerUntilOppEnd(me.Leader, 7000, owner);
        return Task.CompletedTask;
    }
}
