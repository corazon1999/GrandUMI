using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-002 艾狄欧（角色）
/// 【咚!!×1】【攻击时】本次战斗中，对方力量不高于 2000 的角色不能发动【阻挡者】效果。
///
/// 实现说明：
///   - 【咚!!×1】为触发节附带成本，按惯例只实现收益。
///   - OnAttackDeclare 时对对方所有当前力量 ≤2000 的角色施加 CannotBeBlocker(ThisBattle)。
/// </summary>
public class OP03_002_Aidio : IScriptedEffect
{
    public string CardNumber => "OP03-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (ctx.State.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

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
