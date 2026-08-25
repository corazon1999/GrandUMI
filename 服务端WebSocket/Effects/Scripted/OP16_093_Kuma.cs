using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-093 巴索罗缪·大熊。
/// 【登场时】抽2张，丢弃2张手牌。之后，赋予我方1张领袖或角色最多1张休息状态的咚!!。
/// </summary>
public sealed class OP16_093_Kuma : IScriptedEffect
{
    public string CardNumber => "OP16-093";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 强制弃牌按结算时实际手牌尽量执行；选择期间不提前移动，返回后再按实例复核。
        int discardCount = Math.Min(2, me.Hand.Count);
        if (discardCount > 0)
        {
            var candidates = me.Hand.ToList();
            var chosenIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
                $"选择丢弃{discardCount}张手牌",
                candidates.Select(card => card.Id.ToString()).ToList(), discardCount, discardCount,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = candidates
                        .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                        .ToList(),
                });

            var chosen = chosenIds
                .Distinct(StringComparer.Ordinal)
                .Select(id => candidates.FirstOrDefault(card => card.Id.ToString() == id))
                .Where(card => card is not null)
                .Cast<CardInstance>()
                .Take(discardCount)
                .ToList();
            // Mock、超时恢复或非法响应不足时仍按强制效果补足，但绝不重复移动同一实例。
            foreach (var fallback in candidates.Where(card => !chosen.Contains(card)))
            {
                if (chosen.Count >= discardCount) break;
                chosen.Add(fallback);
            }
            foreach (var card in chosen)
                if (me.Hand.Contains(card)) AtomicOps.DiscardHand(me, card);
        }

        if (!me.CostArea.Any(don => don.State == DonState.Rest)) return;
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var targetIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多1张领袖或角色，赋予1张休息状态的咚!!",
            targets.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = targets
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });
        if (targetIds.Count != 1) return;

        // 选择完成后重新从当前场上实例解析；角色已离场时不得把咚附到悬空 ID。
        var target = me.Leader.Id.ToString() == targetIds[0]
            ? me.Leader
            : me.Characters.FirstOrDefault(card => card.Id.ToString() == targetIds[0]);
        if (target is not null)
            AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }
}
