using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-094 Mr.5（杰姆）。
/// 【登场时】双方场上存在当前费用恰为 0 或当前费用不低于 8 的角色时，抽 2 张，再丢弃 1 张手牌。
/// </summary>
public sealed class OP14_094_Mr5 : IScriptedEffect
{
    public string CardNumber => "OP14-094";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        bool conditionMet = Enumerable.Range(0, state.Players.Length)
            .Any(side => state.Players[side].Characters.Any(card =>
            {
                int currentCost = state.CurrentCostOf(side, card);
                return currentCost == 0 || currentCost >= 8;
            }));
        if (!conditionMet) return;

        // 本脚本完整接管该触发，不委托旧 DSL，避免无条件定义再次抽弃。
        await AtomicOps.DrawAsync(state, ctx.OwnerIndex, 2);

        var hand = state.Players[ctx.OwnerIndex].Hand;
        int actual = Math.Min(1, hand.Count);
        if (actual == 0) return;
        var candidates = hand.ToList();
        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "DiscardOwnChosen",
            "丢弃 1 张手牌",
            candidates.Select(card => card.Id.ToString()).ToList(),
            1,
            1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });

        var discard = chosen.Count == 1
            ? hand.FirstOrDefault(card => card.Id.ToString() == chosen[0])
            : hand.FirstOrDefault();
        if (discard is not null) AtomicOps.DiscardHand(state.Players[ctx.OwnerIndex], discard);
    }
}
