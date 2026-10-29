using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-028 猫蝮蛇（角色）
/// 【登场时】对方场上存在7张或更多处于休息状态的卡牌的场合，
///   本回合中，此角色获得【速攻】效果。
///
/// 实现说明 / 简化点：
///   - 统计对方场上休息状态的领袖、角色、舞台与费用区咚；附着咚不属于休息状态。
///   - 条件满足时用 GiveKeyword 赋予自身【速攻】，持续本回合(ThisTurn)。
/// </summary>
public class OP08_028_Nekomamushi : IScriptedEffect
{
    public string CardNumber => "OP08-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        int restedCount = opp.Characters.Count(c => c.IsTapped);
        if (opp.Leader.IsTapped) restedCount++;
        restedCount += opp.StageCards.Count(stage => stage.IsTapped);
        restedCount += opp.CostArea.Count(don => don.State == DonState.Rest);

        if (restedCount >= 7)
        {
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
        }

        return Task.CompletedTask;
    }
}
