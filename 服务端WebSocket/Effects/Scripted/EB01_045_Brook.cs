using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-045 布鲁克（角色 / 地 / 伦巴海盗团）
/// 【登场时】对方场上存在费用为 0 的角色的场合，本回合中，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 前置条件：对方场上存在当前费用为 0 的角色（含临时与持续费用修正）。
///   - 满足则本回合赋予自身【速攻】，用 GiveKeyword(ThisTurn) 即可（无需持续效果）。
/// </summary>
public class EB01_045_Brook : IScriptedEffect
{
    public string CardNumber => "EB01-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.Characters.Any(c => ctx.State.CurrentCostOf(c) == 0))
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
        return Task.CompletedTask;
    }
}
