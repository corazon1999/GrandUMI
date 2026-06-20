using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-004 卓夫（角色 / 炎 / 东海）
/// 【攻击时】直到下个对方的结束阶段结束时为止，我方领袖原本的力量变为7000。
///
/// 实现说明：
///   - "原本力量变为7000"用 ContinuousEffect：PowerDelta = 7000 - 领袖卡面原始力量（领袖单一已知卡，
///     注册时按其 Info.Power 计算固定增量并以 Scope.Filter 限定到该领袖 Id）。
///   - "直到下个对方结束阶段结束"为跨回合有效期：在攻击时（我方回合 baseTurn）注册，有效期延续到
///     对方回合（baseTurn+1）结束，故 Predicate 用 s.TurnCount <= baseTurn + 1 限定。
///   - 每次攻击时刷新（先 RemoveAll 本卡旧效果，重新注册并更新基准回合）。
/// </summary>
public class EB04_004_Dorry : IScriptedEffect
{
    public string CardNumber => "EB04-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        var leader = me.Leader;
        if (leader is null) return Task.CompletedTask;
        var leaderId = leader.Id;
        int delta = 7000 - leader.Info.Power;
        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = true,
                IncludeCharacters = false,
                Filter = c => c.Id == leaderId,
            },
            PowerDelta = delta,
            // 只改领袖：必须在 Predicate 里限定 card.Id==leaderId（引擎不消费 Scope.Filter，
            // 否则会把"7000-领袖基础力量"的增量加到全场每张卡上）
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id == leaderId &&
                s.TurnCount <= baseTurn + 1,
        });

        return Task.CompletedTask;
    }
}
