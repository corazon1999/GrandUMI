using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-097 杰克斯（角色 / 炎）
/// 【登场时】/【攻击时】本回合中，对方无法发动【阻挡者】效果。
///
/// 实现说明：
///   - 「对方本回合无法发动【阻挡者】」= 对对方场上所有角色施加 CannotBeBlocker(ThisTurn)。
///   - 登场时与攻击时均触发；本回合内新登场的对方角色不在此次施加范围，按惯例近似处理。
/// </summary>
public class P_097_Shanks : IScriptedEffect
{
    public string CardNumber => "P-097";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        foreach (var c in opp.Characters)
        {
            AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisTurn);
        }
        return Task.CompletedTask;
    }
}
