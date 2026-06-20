using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-003 斯摩格&达斯琪（角色 / 炎 / 班克禁区·海军）
/// 【速攻】（关键词，由引擎处理）
/// 【对方的回合中】我方拥有《海军》特征的领袖原本的力量变为7000。
///
/// 实现说明：
///   - "原本力量变为7000"用 ContinuousEffect 表达：PowerDelta = 7000 - 领袖卡面原始力量，
///     使评估时领袖基础力量等效变为 7000（贴咚等加成仍另叠加，符合"原本力量"语义）。
///   - 领袖是单一已知卡，故注册时即可按其 Info.Power 计算固定增量，并用 Scope.Filter 限定到该领袖 Id。
///   - 仅在对方回合、且我方领袖含《海军》特征时生效（Predicate 二次判定，应对领袖中途变化的边界）。
///   - 登场时注册，离场由引擎清理；先 RemoveAll 防重复。
/// </summary>
public class EB04_003_Smoker_Tashigi : IScriptedEffect
{
    public string CardNumber => "EB04-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        var leader = me.Leader;
        if (leader is null) return Task.CompletedTask;
        var leaderId = leader.Id;
        int delta = 7000 - leader.Info.Power; // 使原本力量变为 7000

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
            // 否则会把全场所有《海军》卡都变 7000）；HasKeyword 此时即判定该领袖是否含《海军》
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id == leaderId &&
                card.Info.HasKeyword("海军") &&
                s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
