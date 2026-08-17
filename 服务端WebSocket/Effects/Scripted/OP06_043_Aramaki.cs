using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP06-043 荒牧的启动主要效果。</summary>
public sealed class OP06_043_Aramaki : IScriptedEffect
{
    public string CardNumber => "OP06-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        var me = state.Players[ctx.OwnerIndex];
        var key = $"{ctx.Source.Id}-Activated";
        if (me.TurnOnceUsed.Contains(key) || me.Hand.Count == 0) return;

        var candidates = state.Players
            .SelectMany((player, side) => player.Characters.Select(card => (side, card)))
            .Where(item => state.CurrentCostOf(item.side, item.card) <= 2)
            .ToList();
        if (candidates.Count == 0) return;

        var targetExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(item => new
            {
                id = item.card.Id.ToString(),
                number = item.card.Info.Number,
            }).ToList(),
        };
        var selectedTarget = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择1张费用不高于2的角色放回持有者卡组最下方",
            candidates.Select(item => item.card.Id.ToString()).ToList(), 1, 1, targetExtra);
        if (selectedTarget.Count != 1) return;

        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(card => new
            {
                id = card.Id.ToString(),
                number = card.Info.Number,
            }).ToList(),
        };
        var selectedDiscard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "丢弃1张手牌作为发动成本",
            me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1, discardExtra);
        if (selectedDiscard.Count != 1) return;

        var target = candidates.First(item => item.card.Id.ToString() == selectedTarget[0]);
        var discard = me.Hand.First(card => card.Id.ToString() == selectedDiscard[0]);
        AtomicOps.DiscardHand(me, discard);

        if (await AtomicOps.TryEffectLeaveGuard(state, target.side, target.card, ctx.Prompts, "deck-bottom"))
            return;
        AtomicOps.ReturnFieldToDeckBottom(state, target.side, target.card);
        AtomicOps.AddPowerThisTurn(ctx.Source, 3000);
        me.TurnOnceUsed.Add(key);
    }
}
