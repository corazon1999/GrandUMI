using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-102 斯摩格（角色）
/// 此角色不会因效果而被KO。
/// 【攻击时】场上存在费用为 0 的角色的场合，本次战斗中，此角色的力量+2000。
///
/// 实现说明：
///   - 持续"不会因效果被KO"用 ContinuousEffect.KoGuard="effect"（OnEnterField 注册）。
///   - 【攻击时】条件"场上存在费用为 0 的角色"用 CurrentCostOf 判定；成立则本次战斗 +2000
///     （AddPowerThisBattle）。
/// </summary>
public class OP02_102_Smoker : IScriptedEffect
{
    public string CardNumber => "OP02-102";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                KoGuard = "effect",
                Predicate = (s, sideIdx, card) => card.Id == selfId,
            });
            return Task.CompletedTask;
        }

        // 【攻击时】
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        bool hasZeroCost =
            me.Characters.Any(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) == 0) ||
            opp.Characters.Any(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) == 0);
        if (hasZeroCost)
            AtomicOps.AddPowerThisBattle(self, 2000);

        return Task.CompletedTask;
    }
}
