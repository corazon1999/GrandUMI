using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-025 克里克（角色）
/// 【登场时】可以丢弃我方的1张手牌：将对方最多2张费用不高于4且处于休息状态的角色KO。
/// 【咚!!×1】此角色获得【双重攻击】效果。
///
/// 实现说明：
///   - 【登场时】"丢弃1手牌"为发动成本：用 ConfirmOptional 询问，确认后弃1张手牌再KO最多2张目标。
///   - 【咚!!×1】双重攻击为贴咚条件持续关键词，用 ContinuousEffect.GrantKeyword 注册（自身被贴咚≥1时生效）。
/// </summary>
public class OP03_025_Krieg : IScriptedEffect
{
    public string CardNumber => "OP03-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // ── 【咚!!×1】持续：自身被贴咚≥1 时获得【双重攻击】 ──
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "双重攻击",
            Predicate = (s, side, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        // ── 【登场时】可选：丢弃1张手牌，KO对方最多2张费用≤4且休息的角色 ──
        var targets = opp.Characters
            .Where(c => c.IsTapped && ctx.State.CurrentCostOf(1 - owner, c) <= 4)
            .ToList();
        if (targets.Count == 0) return;
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克里克【登场时】：丢弃我方1张手牌，将对方最多2张费用≤4且处于休息状态的角色KO？");
        if (!use) return;

        // 成本：丢弃1张手牌
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方1张手牌（成本）", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (disc.Count < 1) return;
        var discardCard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, discardCard);

        // 效果：KO对方最多2张
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多2张费用≤4且处于休息状态的角色KO",
            targets.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(2, targets.Count));
        foreach (var id in chosen)
        {
            var tgt = targets.First(c => c.Id.ToString() == id);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
