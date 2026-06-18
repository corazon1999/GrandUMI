using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-017 波雅·汉库珂（角色 / 光 / FILM・王下七武海・九蛇海盗团 / cost5 power7000）
/// 【登场时】可以丢弃我方手牌中1张拥有【触发】效果的卡牌：
///   直到下个对方的结束阶段结束时为止，对方处于休息状态的领袖
///   或对方最多1张“蒙奇·D·路飞”以外的角色无法攻击。
/// （trigger 字段的【触发】KO 由引擎独立处理，本脚本仅实现【登场时】主动效果。）
///
/// 实现说明 / 简化点：
///   - 成本：丢弃1张「拥有【触发】效果」的手牌（CardInfo.Trigger 非空）。
///   - 收益为二选一：①对方处于休息状态的领袖无法攻击；②对方最多1张非“路飞”角色无法攻击。
///     用 CannotAttack 限制 + KeywordDuration.UntilNextOpponentEndPhase（与“直到下个对方结束阶段”对应）。
///   - 若对方领袖未处于休息状态则该分支无意义，按需仅在领袖休息时提供该分支。
/// </summary>
public class PRB02_017_BoaHancock : IScriptedEffect
{
    public string CardNumber => "PRB02-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本候选：拥有【触发】效果的手牌
        var triggerCards = me.Hand.Where(c => !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (triggerCards.Count == 0) return;

        // 收益候选
        bool leaderRested = opp.Leader.IsTapped;
        var charTargets = opp.Characters.Where(c => !c.MatchesName("蒙奇·D·路飞")).ToList();
        if (!leaderRested && charTargets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "汉库珂【登场时】：丢弃1张拥有【触发】的手牌，使对方休息领袖或最多1张非路飞角色无法攻击？");
        if (!use) return;

        // 支付成本：丢弃1张拥有【触发】的手牌
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = triggerCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张拥有【触发】效果的手牌作为成本",
            triggerCards.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (pick.Count == 0) return;
        var disc = triggerCards.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, disc);

        // 收益：二选一（仅当领袖休息时提供领袖分支）
        if (leaderRested && charTargets.Count > 0)
        {
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择令其无法攻击的目标",
                new[] { "对方休息状态的领袖", "对方最多1张非路飞角色" });
            if (opt == 0)
            {
                AtomicOps.AddRestriction(opp.Leader, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
                return;
            }
        }
        else if (leaderRested)
        {
            AtomicOps.AddRestriction(opp.Leader, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
            return;
        }

        // 角色分支：最多1张非路飞角色无法攻击
        if (charTargets.Count == 0) return;
        var cpick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张“路飞”以外的角色，使其无法攻击",
            charTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (cpick.Count > 0)
        {
            var tgt = charTargets.First(c => c.Id.ToString() == cpick[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
