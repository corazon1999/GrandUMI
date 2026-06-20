using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-007 罗罗诺亚·佐罗（角色 / 炎 / 艾格赫德·草帽一伙）
/// 【登场时】直到下个对方的结束阶段结束时为止，我方领袖力量 +2000。
/// 【启动主要】【每回合1次】对方场上存在力量为 8000 或更高的角色的场合，
///   本回合中，此角色获得【速攻：角色】效果。
///
/// 实现说明：
///   - 【登场时】领袖 +2000「直到下个对方结束阶段」跨回合，用 ContinuousEffect + TurnCount 时效
///     （baseTurn..+2，即我方回合与紧接的对方回合内有效）。Scope 限定我方领袖。
///   - 【启动主要】条件「对方存在当前力量≥8000角色」用 CurrentPowerOf 判定；满足则本回合赋予自身
///     【速攻】（引擎仅识别"速攻"用于放行登场回合攻击；"速攻：角色"的只可打角色限制为简化省略）。
///   - 每回合1次用 TurnOnceUsed。
/// </summary>
public class EB04_007_Zoro : IScriptedEffect
{
    public string CardNumber => "EB04-007";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var leaderId = me.Leader.Id;
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
                PowerDelta = 2000,
                // 只加领袖：必须在 Predicate 里限定 card.Id==leaderId（引擎不消费 Scope.Filter，
                // 否则会变成全场+2000）
                Predicate = (s, sideIdx, card) =>
                    sideIdx == owner && card.Id == leaderId && s.TurnCount <= baseTurn + 1,
            });
            return Task.CompletedTask;
        }

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = self.Info.Number + "-act" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

            int oppIdx = 1 - owner;
            bool hasBig = opp.Characters.Any(c => ctx.State.CurrentPowerOf(oppIdx, c) >= 8000);
            if (!hasBig) return Task.CompletedTask;

            me.TurnOnceUsed.Add(key);
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
