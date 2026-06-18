using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-013 普林斯·格鲁斯（角色）
/// 【攻击时】本回合中，对方所有力量不高于 2000 的角色不能发动【阻挡者】效果。
///
/// 实现说明：
///   - DSL 的 AddRestriction 仅作用单一目标，这里直接遍历对方所有当前力量≤2000 的角色，
///     逐张施加 CannotBeBlocker（本回合），等价于对全体满足条件的角色施加限制。
///   - 力量阈值用 ctx.State.CurrentPowerOf 取含修正后的实时力量判定。
/// </summary>
public class OP11_013_PrinceGrus : IScriptedEffect
{
    public string CardNumber => "OP11-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        foreach (var c in opp.Characters)
        {
            if (ctx.State.CurrentPowerOf(oppIdx, c) <= 2000)
            {
                AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisTurn);
            }
        }

        return Task.CompletedTask;
    }
}
