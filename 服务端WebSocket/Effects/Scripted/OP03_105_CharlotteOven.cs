using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-105 夏洛特·烤箱（角色）
/// 【咚!!×1】【攻击时】可以丢弃我方手牌中1张拥有【触发】效果的卡牌：本次战斗中，此角色的力量+3000。
/// 实现：OnAttackDeclare，需自身附着咚!!≥1；可选成本=丢弃手牌中1张带【触发】的卡，支付后+3000(本次战斗)。
/// </summary>
public class OP03_105_CharlotteOven : IScriptedEffect
{
    public string CardNumber => "OP03-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】发动条件
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 成本候选：手牌中拥有【触发】效果的卡
        var triggerCards = me.Hand.Where(c => !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (triggerCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "烤箱【攻击时】：丢弃1张拥有【触发】效果的手牌，使此角色本次战斗+3000？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = triggerCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃1张拥有【触发】效果的手牌",
            triggerCards.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count < 1) return;

        var discard = triggerCards.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);
        AtomicOps.AddPowerThisBattle(self, 3000);
    }
}
