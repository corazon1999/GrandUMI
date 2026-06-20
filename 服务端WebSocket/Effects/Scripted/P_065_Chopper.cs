using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-065 托尼托尼·乔巴（角色 / 地 3 费 4000 / 动物・草帽一伙）
/// 【攻击时】对方场上存在费用为0的角色的场合，
///   直到下个我方的回合开始时为止，此角色的力量+2000。
///
/// 实现说明：
///   - 攻击时若对方场上存在「当前费用为0」的角色，则给此角色 +2000，效果持续到下个我方回合开始。
///   - 该时长跨越本回合 + 对方回合，至下个我方回合开始失效，非「本回合/本次战斗」，
///     故用 ContinuousEffect 注册，Predicate 以注册时回合数 baseTurn 限定有效期：
///     我方回合 N 触发 → 对方回合 N+1 → 我方回合 N+2 开始失效，即 s.TurnCount <= baseTurn + 1 期间生效。
///   - 条件在攻击时一次性判定（满足才注册）；离场由引擎自动清理。
/// </summary>
public class P_065_Chopper : IScriptedEffect
{
    public string CardNumber => "P-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        // 条件：对方场上存在当前费用为 0 的角色
        bool hasCost0 = opp.Characters.Any(c => ctx.State.CurrentCostOf(oppIdx, c) == 0);
        if (!hasCost0) return Task.CompletedTask;

        var self = ctx.Source;
        var selfId = self.Id;
        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.TurnCount <= baseTurn + 1,
        });
        return Task.CompletedTask;
    }
}
