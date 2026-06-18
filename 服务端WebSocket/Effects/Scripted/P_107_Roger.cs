using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-107 高路·D·罗杰（角色 / 暗 / 海盗王・罗杰海盗团，cost8 power10000）
/// 【登场时】我方或对方场上存在 10 张咚!! 的场合，直到下个对方的结束阶段结束时为止，我方领袖力量+2000。
///
/// 实现说明：
///   - 条件「我方或对方场上存在 10 张咚!!」近似为任一方费用区咚总数 >=10。
///   - 「直到下个对方的结束阶段结束时」用 ContinuousEffect.PowerDelta + TurnCount 有效期：
///     登场（我方回合）注册后，覆盖至下个对方回合结束 → 有效期约为 baseTurn..baseTurn+1。
///   - 仅当条件成立时注册；不成立则不注册。
/// </summary>
public class P_107_Roger : IScriptedEffect
{
    public string CardNumber => "P-107";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        bool tenDon = me.CostArea.Count >= 10 || opp.CostArea.Count >= 10;
        if (!tenDon) return Task.CompletedTask;

        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 2000,
            // 我方领袖；有效期至下个对方回合结束（baseTurn 我方回合 → baseTurn+1 对方回合）
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == s.Players[owner].Leader.Id &&
                s.TurnCount <= baseTurn + 1,
        });

        return Task.CompletedTask;
    }
}
