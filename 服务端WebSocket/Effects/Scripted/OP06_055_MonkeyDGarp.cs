using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-055 蒙奇·D·戈普（角色 / 水 5 费 7000）
/// 【咚!!×2】【攻击时】我方的手牌不多于 4 张的场合，本次战斗中，对方无法发动【阻挡者】效果。
///
/// 实现说明：
///   - 【咚!!×2】门槛：此角色被赋予的咚!! ≥ 2（AttachedDonCount(self.Id)）。
///   - 仅当此角色为本次攻击的攻击者时生效。
///   - "对方无法发动【阻挡者】本次战斗" → 对对方所有角色加 CannotBeBlocker（ThisBattle），
///     与规范 12.3 一致。
/// </summary>
public class OP06_055_MonkeyDGarp : IScriptedEffect
{
    public string CardNumber => "OP06-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅当此角色为本次攻击的攻击者时
        var b = ctx.State.CurrentBattle;
        if (b is null || b.AttackerCardId != self.Id) return Task.CompletedTask;

        // 【咚!!×2】：此角色被赋予的咚!! ≥ 2
        if (me.AttachedDonCount(self.Id) < 2) return Task.CompletedTask;

        // 手牌不多于 4 张
        if (me.Hand.Count > 4) return Task.CompletedTask;

        // 本次战斗中，对方无法发动【阻挡者】
        foreach (var c in opp.Characters)
        {
            AtomicOps.AddRestriction(c, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisBattle);
        }

        return Task.CompletedTask;
    }
}
