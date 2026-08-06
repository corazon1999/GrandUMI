using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-020 火拳（事件）
/// 【主要】本回合中，我方领袖力量 +3000。直到下个对方的结束阶段结束时为止，
///   对方最多 1 张角色力量 -8000。之后，可以丢弃我方的 2 张手牌；
///   若丢弃了 2 张，则将对方最多 1 张力量不高于 0 的角色 KO。
///
/// 实现说明：
///   - 跨回合降力使用 AddPowerUntilOppEnd，并记录效果施加方以便在正确的结束阶段清除。
///   - 弃牌属于“之后”的可选效果。按官方 Q&amp;A，手牌不足 2 张时仍可丢弃现有手牌，
///     但只有实际丢弃满 2 张时才进入 KO 段。
///   - KO 候选在降力及弃牌完成后按实时力量重新计算。
/// </summary>
public class OP15_020_FireFist : IScriptedEffect
{
    public string CardNumber => "OP15-020";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 1. 本回合中，我方领袖力量 +3000。
        AtomicOps.AddPowerThisTurn(me.Leader, 3000);

        // 2. 对方最多 1 张角色力量 -8000，持续到下个对方结束阶段结束。
        var weakenCandidates = opp.Characters.ToList();
        if (weakenCandidates.Count > 0)
        {
            var weakenExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = weakenCandidates
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var weakenChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，力量 -8000，直到下个对方的结束阶段结束",
                weakenCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1, weakenExtra);
            if (weakenChoice.Count > 0)
            {
                var target = weakenCandidates.FirstOrDefault(c => c.Id.ToString() == weakenChoice[0]);
                if (target is not null)
                    AtomicOps.AddPowerUntilOppEnd(target, -8000, ctx.OwnerIndex);
            }
        }

        // 3. 可选丢弃手牌。没有手牌时无法执行这一段。
        if (me.Hand.Count == 0) return;

        int discardCount = Math.Min(2, me.Hand.Count);
        string confirmText = discardCount == 2
            ? "火拳：是否丢弃 2 张手牌，以 KO 对方最多 1 张力量不高于 0 的角色？"
            : "火拳：当前只有 1 张手牌。是否将其丢弃？未丢弃满 2 张时不能执行 KO。";
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, confirmText)) return;

        var handSnapshot = me.Hand.ToList();
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = handSnapshot
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var discardChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            $"丢弃 {discardCount} 张手牌",
            handSnapshot.Select(c => c.Id.ToString()).ToList(), discardCount, discardCount, discardExtra);

        // 精确数量选择超时或返回不完整时，沿用项目惯例，以当前候选顺序补足。
        var discarded = discardChoice
            .Select(id => handSnapshot.FirstOrDefault(c => c.Id.ToString() == id))
            .Where(c => c is not null)
            .Cast<CardInstance>()
            .DistinctBy(c => c.Id)
            .Take(discardCount)
            .ToList();
        foreach (var fallback in handSnapshot.Where(c => discarded.All(d => d.Id != c.Id)))
        {
            if (discarded.Count >= discardCount) break;
            discarded.Add(fallback);
        }
        foreach (var card in discarded)
            AtomicOps.DiscardHand(me, card);

        // 官方规则：未实际丢弃满 2 张时，不能执行后续 KO。
        if (discarded.Count < 2) return;

        var koCandidates = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 0)
            .ToList();
        if (koCandidates.Count == 0) return;

        var koExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = koCandidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var koChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量不高于 0 的角色 KO",
            koCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1, koExtra);
        if (koChoice.Count == 0) return;

        var koTarget = koCandidates.FirstOrDefault(c => c.Id.ToString() == koChoice[0]);
        if (koTarget is not null)
            AtomicOps.KO(ctx.State, oppIdx, koTarget);
    }
}
