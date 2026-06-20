using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-002 贝洛·贝蒂（领航）
/// 【启动主要】【每回合1次】可以丢弃我方手牌中 1 张拥有《革命军》特征的卡牌：
///   本回合中，我方最多 3 张拥有《革命军》特征或【触发】的角色力量 +3000。
///
/// 实现说明 / 简化点：
///   - 成本为丢弃 1 张《革命军》手牌，用 ConfirmOptional + ChooseCards(手牌) 选择后 DiscardHand。
///   - 受益对象筛选：角色 Info.HasKeyword("革命军") 或拥有【触发】(Info.Trigger 非空)。
///   - 每回合 1 次用 TurnOnceUsed 标记；多目标用 ChooseCards(0~3) 后逐张 AddPowerThisTurn。
/// </summary>
public class OP05_002_Belo_Betty : IScriptedEffect
{
    public string CardNumber => "OP05-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中拥有《革命军》特征的卡牌
        var costCards = me.Hand.Where(c => c.Info.HasKeyword("革命军")).ToList();
        if (costCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "贝洛·贝蒂【启动主要】：丢弃 1 张《革命军》手牌，使我方最多 3 张《革命军》或【触发】角色 +3000？");
        if (!use) return;

        // 支付成本：丢弃 1 张《革命军》手牌
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var paid = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张《革命军》手牌作为成本",
            costCards.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (paid.Count < 1) return;
        var costCard = costCards.First(c => c.Id.ToString() == paid[0]);
        AtomicOps.DiscardHand(me, costCard);

        me.TurnOnceUsed.Add(key);

        // 受益对象：我方《革命军》或拥有【触发】的角色
        var targets = me.Characters.Where(c =>
            c.Info.HasKeyword("革命军") ||
            !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 3 张《革命军》或【触发】角色，本回合 +3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 3);
        foreach (var id in chosen)
        {
            var t = targets.First(c => c.Id.ToString() == id);
            AtomicOps.AddPowerThisTurn(t, 3000);
        }
    }
}
