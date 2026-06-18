using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-112 雷藏（角色）
/// 【攻击时】可以丢弃我方的1张手牌：将对方最多1张咚!!转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本：丢弃我方1张手牌（无手牌则无法发动）。
///   - 收益：将对方1张处于活跃状态的咚!!转为休息状态（咚!!卡彼此无差异，直接横置1张活跃咚）。
/// </summary>
public class OP06_112_Raizo : IScriptedEffect
{
    public string CardNumber => "OP06-112";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 至少需要 1 张手牌作为成本，且对方需有 1 张活跃咚!!
        if (me.Hand.Count == 0) return;
        var activeDon = opp.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "雷藏【攻击时】：丢弃我方1张手牌，将对方1张咚!!转为休息状态？");
        if (!use) return;

        // 成本：丢弃我方1张手牌
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        // 收益：将对方1张活跃咚!!转为休息状态
        var don = opp.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is not null) don.State = DonState.Rest;
    }
}
