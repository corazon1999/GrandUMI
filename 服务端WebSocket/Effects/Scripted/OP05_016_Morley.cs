using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-016 莫里（角色）
/// 【攻击时】此角色的力量为 7000 或更高的场合，本次战斗中，对方无法发动【阻挡者】效果。
///
/// 实现说明 / 简化点：
///   - 力量阈值用 ctx.State.CurrentPowerOf 取含修正后的实时力量判定（≥7000 才发动）。
///   - "对方无法发动【阻挡者】" 等价于对对方所有角色施加 CannotBeBlocker（本次战斗），
///     与 OP11-013 思路一致，只是范围为对方全体且仅限本次战斗(ThisBattle)。
///   - 卡面【触发】是独立的另一段触发效果（弃手牌后登场），与本主效果无关，未在此实现。
/// </summary>
public class OP05_016_Morley : IScriptedEffect
{
    public string CardNumber => "OP05-016";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        if (ctx.State.CurrentPowerOf(ctx.OwnerIndex, ctx.Source) < 7000) return Task.CompletedTask;

        foreach (var c in opp.Characters)
        {
            AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        }

        return Task.CompletedTask;
    }
}
