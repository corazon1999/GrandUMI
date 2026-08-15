using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-001 光月御殿（领航 / 炎·风 / 和之国·光月家）
/// 规则上，我方所有拥有《和之国》特征且没有反击的角色卡牌变为拥有反击+1000。
/// 【咚!!×1】【攻击时】我方场上存在费用为5或更高且拥有《和之国》特征的角色的场合，
///   直到下个我方的回合开始时为止，此领袖的力量+1000。
///
/// 实现说明 / 简化点：
///   - 手牌中的持续反击值由 HandStaticCounter 实时计算。
///   - "直到下个我方回合开始时"等价为跨越当前回合并在紧随的对方结束阶段清除。
/// </summary>
public class EB01_001_KozukiOden : IScriptedEffect
{
    public string CardNumber => "EB01-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

        // 仅领袖攻击时本效果生效
        if (ctx.Source.Id != me.Leader.Id) return Task.CompletedTask;

        bool cond = me.Characters.Any(c => ctx.State.CurrentCostOf(c) >= 5 && c.Info.HasKeyword("和之国"));
        if (cond)
            AtomicOps.AddPowerUntilOppEnd(me.Leader, 1000, ctx.OwnerIndex);

        return Task.CompletedTask;
    }
}
