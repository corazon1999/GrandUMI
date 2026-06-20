using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-064 爱比达（角色 / 水 / 巴奇海盗团 / 2 费 3000）
/// 【咚!!×1】【攻击时】可以丢弃我方的 1 张手牌：将对方最多 1 张费用不高于 3 的角色放回持有者的手牌。
///
/// 实现说明：
///   - 触发：攻击时（OnAttackDeclare），可选发动。
///   - 成本：丢弃我方 1 张手牌（DiscardHand）。
///   - 收益：将对方最多 1 张费用≤3 的角色退回其手牌（BounceToHand）。
/// </summary>
public class OP01_064_Abida : IScriptedEffect
{
    public string CardNumber => "OP01-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        if (me.Hand.Count == 0) return;
        var targets = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "爱比达【攻击时】：丢弃 1 张手牌，将对方最多 1 张费用≤3 的角色放回手牌？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌（由我方选弃哪张）
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌（成本）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (disc.Count == 0) return;
        var toDiscard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 收益：退回对方最多 1 张费用≤3 角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色放回手牌",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
