using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-001 光月御殿（领航 / 炎·风 / 和之国·光月家）
/// 规则上，我方所有拥有《和之国》特征且没有反击的角色卡牌变为拥有反击+1000。 ← 反击赋予引擎无持续通道，未实现该段。
/// 【咚!!×1】【攻击时】我方场上存在费用为5或更高且拥有《和之国》特征的角色的场合，
///   直到下个我方的回合开始时为止，此领袖的力量+1000。
///
/// 实现说明 / 简化点：
///   - 仅实现可表达的【攻击时】力量段：条件满足时给领袖 +1000。
///   - "直到下个我方回合开始时"用本回合力量加成近似（AddPowerThisTurn），与多数实现一致。
///   - "规则上赋予反击+1000"为持续的反击数值赋予，引擎无对应持续通道，未实现该部分。
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

        bool cond = me.Characters.Any(c => c.Info.Cost >= 5 && c.Info.HasKeyword("和之国"));
        if (cond)
            AtomicOps.AddPowerThisTurn(me.Leader, 1000);

        return Task.CompletedTask;
    }
}
