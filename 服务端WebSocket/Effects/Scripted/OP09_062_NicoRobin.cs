using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-062 妮古·罗宾（领航）
/// 【流放】(此卡牌给予伤害的场合，不发动触发效果将该卡牌放置到废弃区) —— 引擎未实现该关键词，此处不处理。
/// 【攻击时】可以丢弃我方手牌中 1 张拥有【触发】效果的卡牌：从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
/// - 仅实现【攻击时】主动效果；【流放】关键词引擎无对应机制，忽略。
/// - 成本"丢弃 1 张拥有【触发】效果的手牌"用 DiscardHand 支付；候选用 c.Info.Trigger 非空判断。
/// - 收益"追加最多 1 张休息状态咚"用 RefreshDonFromDeck(me, 1, DonState.Rest)。
/// </summary>
public class OP09_062_NicoRobin : IScriptedEffect
{
    public string CardNumber => "OP09-062";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：手牌中拥有【触发】效果的卡牌
        var cands = me.Hand
            .Where(c => !string.IsNullOrWhiteSpace(c.Info.Trigger))
            .ToList();
        if (cands.Count == 0) return;
        if (me.DonDeck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗宾【攻击时】：丢弃 1 张拥有【触发】效果的手牌，从咚!!卡组追加 1 张休息状态的咚!!？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardTriggerCard",
            "丢弃 1 张拥有【触发】效果的手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count == 0) return; // 放弃 → 不支付成本

        var discard = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
