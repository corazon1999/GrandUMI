using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-043 库赞
/// 【登场时】可以丢弃我方的 1 张手牌：直到下个对方的结束阶段结束时为止，对方最多 1 张角色无法攻击。
///
/// 简化点：
/// - 卡面常驻被动"我方手牌为 5 张或更多的场合，此角色的费用+1"未实现（引擎条件式静态费用修正缺口）。
/// - "丢弃 1 张手牌"为可选支付：玩家在手牌选择中选 0 张则跳过整个效果，选 1 张则继续指定目标。
/// </summary>
public class OP12_043_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP12-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        if (me.Hand.Count == 0) return;

        // 可选支付：丢弃 1 张手牌（选 0 张 = 不发动）
        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "可以丢弃 1 张手牌以发动效果（选 0 张跳过）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1, handExtra);
        if (discardPick.Count == 0) return;

        var discardCard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardPick[0]);
        if (discardCard is null) return;
        AtomicOps.DiscardHand(me, discardCard);

        // 对方最多 1 张角色，直到下个对方结束阶段结束时为止无法攻击
        if (opp.Characters.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "对方最多 1 张角色直到下个对方结束阶段结束时无法攻击",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = opp.Characters.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
