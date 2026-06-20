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
///   - 【对方回合中】-3000：OnEnterField 时注册持续 PowerDelta=-3000，Predicate 限对方回合，仅作用自身。
///   - 【攻击时】「领袖原本力量变为7000」：OnAttackDeclare 时注册一条作用于我方领袖的持续效果，
///     PowerDelta = (7000 - 领袖卡面原始力量)，近似「原本力量变为7000」；有效期「直到下个对方回合结束」
///     用 TurnCount 限制（注册时记录基准回合，下个对方回合结束约为 baseTurn+2 内有效）。
///     重复发动前按 SourceCardId 去重，避免叠加。
/// </summary>
public class P_092_Koby : IScriptedEffect
{
    public string CardNumber => "P-092";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 【对方回合中】此角色力量-3000（持续）
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString() + "-self");
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString() + "-self",
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = -3000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId && s.CurrentTurnPlayer != owner,
            });
            return Task.CompletedTask;
        }

        // OnAttackDeclare：领袖《海军》时，领袖原本力量变为7000，直到下个对方回合结束
        if (!me.Leader.Info.HasKeyword("海军")) return Task.CompletedTask;

        int leaderBase = me.Leader.Info.Power;
        int baseTurn = ctx.State.TurnCount;
        var leaderId = me.Leader.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString() + "-leader");
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString() + "-leader",
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 7000 - leaderBase,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == leaderId && s.TurnCount <= baseTurn + 2,
        });
        return Task.CompletedTask;
    }
}
