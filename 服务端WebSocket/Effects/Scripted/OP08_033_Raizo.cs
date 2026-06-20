using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-033 罗迪（角色）
/// 【登场时】我方领袖拥有《纯毛族》特征，且对方场上存在7张或更多处于休息状态的卡牌的场合，
///   将对方最多1张处于休息状态且费用不高于2的角色KO。
///
/// 实现说明 / 简化点：
///   - "处于休息状态的卡牌"统计对方领袖/角色/舞台中 IsTapped 的卡，加上费用区中休息状态的咚!!。
/// </summary>
public class OP08_033_Raizo : IScriptedEffect
{
    public string CardNumber => "OP08-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件1：我方领袖拥有《纯毛族》特征
        if (!me.Leader.Info.HasKeyword("纯毛族")) return;

        // 条件2：对方场上≥7张处于休息状态的卡牌
        int restedCards = 0;
        if (opp.Leader != null && opp.Leader.IsTapped) restedCards++;
        restedCards += opp.Characters.Count(c => c.IsTapped);
        if (opp.StageCard != null && opp.StageCard.IsTapped) restedCards++;
        restedCards += opp.CostArea.Count(d => d.State == DonState.Rest);
        if (restedCards < 7) return;

        // 效果：将对方最多1张休息状态且费用≤2的角色KO
        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张休息状态且费用≤2的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
