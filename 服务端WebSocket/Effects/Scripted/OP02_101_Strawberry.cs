using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-101 草莓（角色）
/// 【攻击时】场上存在费用为 0 的角色的场合，本次战斗中，对方无法发动费用不高于 5 的角色的【阻挡者】效果。
///
/// 实现说明：
///   - 条件"场上存在费用为 0 的角色"用 CurrentCostOf 判定双方场上任一角色当前费用是否为 0。
///   - "对方≤5费角色无法发动【阻挡者】" 用 AddRestriction(CannotBeBlocker, ThisBattle)
///     施加到对方所有当前费用≤5 的角色。
/// </summary>
public class OP02_101_Strawberry : IScriptedEffect
{
    public string CardNumber => "OP02-101";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        bool hasZeroCost =
            me.Characters.Any(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) == 0) ||
            opp.Characters.Any(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) == 0);
        if (!hasZeroCost) return Task.CompletedTask;

        foreach (var c in opp.Characters)
        {
            if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 5)
                AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        }

        return Task.CompletedTask;
    }
}
