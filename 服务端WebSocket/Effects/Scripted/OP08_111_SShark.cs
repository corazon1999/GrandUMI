using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-111 S-鲨鱼（角色，光）
/// 【咚!!×1】【攻击时】本次战斗中，对方无法发动【阻挡者】效果。
///
/// 实现说明 / 简化点：
///   - 触发节无法表达【咚!!×1】可选成本，按惯例只实现收益（不要求/不消耗咚）。
///   - "对方无法发动【阻挡者】"实现为：对当前对方场上所有角色赋予本次战斗的
///     "无法作为阻挡者"限制（CannotBeBlocker），从而本次战斗对方无法用阻挡者拦截。
/// </summary>
public class OP08_111_SShark : IScriptedEffect
{
    public string CardNumber => "OP08-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (ctx.State.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        foreach (var c in opp.Characters)
        {
            AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        }
        return Task.CompletedTask;
    }
}
