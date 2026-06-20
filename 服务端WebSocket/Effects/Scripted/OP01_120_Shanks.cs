using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-120 杰克斯（角色）
/// 【速攻】（纯关键词，引擎处理，本脚本不实现）
/// 【攻击时】本次战斗中，对方力量不高于 2000 的角色不能发动【阻挡者】效果。
///
/// 实现说明：
///   - 在 OnAttackDeclare 触发，对对方所有当前力量 ≤2000 的角色施加
///     CannotBeBlocker（ThisBattle）限制，使其本次战斗无法作为阻挡者。
///   - 力量判定用 ctx.State.CurrentPowerOf（含持续效果）。
/// </summary>
public class OP01_120_Shanks : IScriptedEffect
{
    public string CardNumber => "OP01-120";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        foreach (var c in opp.Characters)
        {
            if (ctx.State.CurrentPowerOf(oppIdx, c) <= 2000)
            {
                AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
            }
        }

        return Task.CompletedTask;
    }
}
