using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-118 杰丽·邦妮（角色 / 超新星·邦妮海盗团）
/// 【阻挡者】（由卡牌关键字数据处理，无需脚本）
/// 【登场时】我方场上存在 8 张或更多处于休息状态的卡牌的场合，
///   抽取 2 张卡牌，丢弃我方的 1 张手牌。之后，将我方最多 1 张咚!! 转为活跃状态。
///
/// 说明：
///   - "处于休息状态的卡牌"统计我方场上休息的角色 + 休息的舞台 + 休息状态的咚!!（DonState.Rest）。
///   - 抽 2 弃 1 为强制（满足条件即触发）；丢弃 1 张经 extra.choiceCards 下发卡面。
///   - "将我方最多 1 张咚!! 转为活跃状态"实现为把 1 张休息咚改为活跃（可选，没有休息咚则跳过）。
/// </summary>
public class OP12_118_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP12-118";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 统计我方场上处于休息状态的卡牌数量：休息角色 + 休息舞台 + 休息咚!!
        int restedChars = me.Characters.Count(c => c.IsTapped);
        int restedStage = (me.StageCard is not null && me.StageCard.IsTapped) ? 1 : 0;
        int restedDon = me.RestDonCount;
        int restedTotal = restedChars + restedStage + restedDon;

        if (restedTotal < 8) return;

        // 抽取 2 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 丢弃我方 1 张手牌（强制；手牌为空则跳过）
        if (me.Hand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "BonneyDiscard",
                "丢弃我方 1 张手牌",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            var card = chosen.Count > 0
                ? me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0])
                : me.Hand[0];
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }

        // 之后：将我方最多 1 张咚!! 转为活跃状态（把 1 张休息咚改为活跃）
        var restDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (restDon is not null) restDon.State = DonState.Active;
    }
}
