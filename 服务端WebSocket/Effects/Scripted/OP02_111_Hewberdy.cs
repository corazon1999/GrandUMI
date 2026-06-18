using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-111 赫波迪（角色）
/// 【攻击时】我方场上存在"强高"的场合，本次战斗中，此角色的力量+3000。
///
/// 实现说明：
///   - 条件"我方场上存在强高"用 me.Characters.Any(c => c.MatchesName("强高"))。
///   - 成立则本次战斗 +3000（AddPowerThisBattle）。
/// </summary>
public class OP02_111_Hewberdy : IScriptedEffect
{
    public string CardNumber => "OP02-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Characters.Any(c => c.MatchesName("强高")))
            AtomicOps.AddPowerThisBattle(ctx.Source, 3000);
        return Task.CompletedTask;
    }
}
