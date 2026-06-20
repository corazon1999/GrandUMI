using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-061 莫里（角色）
/// 【攻击时】我方手牌不多于 1 张的场合，本次战斗中，对方无法发动费用不高于 5 的角色的【阻挡者】效果。
///
/// 实现：满足手牌≤1 张时，对对方场上所有费用≤5 的角色施加 CannotBeBlocker（本次战斗）。
/// </summary>
public class OP02_061_Moria : IScriptedEffect
{
    public string CardNumber => "OP02-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Hand.Count > 1) return Task.CompletedTask;

        foreach (var c in opp.Characters)
        {
            if (c.Info.Cost <= 5)
                AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        }
        return Task.CompletedTask;
    }
}
