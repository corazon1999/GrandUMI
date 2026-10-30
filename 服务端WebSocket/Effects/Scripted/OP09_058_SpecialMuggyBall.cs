using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>马奇弹主要效果由对手选自身角色；生命触发由发动方选择双方角色。</summary>
public sealed class OP09_058_SpecialMuggyBall : IScriptedEffect
{
    public string CardNumber => "OP09-058";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        bool main = ctx.Trigger == EffectTrigger.EventMain;
        int chooser = main ? 1 - ctx.OwnerIndex : ctx.OwnerIndex;
        int limit = main ? 6 : 3;
        var candidates = ctx.State.Players.SelectMany((player, owner) => player.Characters
            .Where(card => (!main || owner != ctx.OwnerIndex) && ctx.State.CurrentCostOf(owner, card) <= limit)
            .Select(card => (owner, card))).ToList();
        if (candidates.Count == 0) return;
        var answer = await ctx.Prompts.ChooseCards(chooser, main ? "OwnCharacter" : "AnyCharacter",
            main ? "选择我方1张费用不高于6的角色放回手牌" : "将最多1张费用不高于3的角色放回其持有者手牌",
            candidates.Select(item => item.card.Id.ToString()).ToList(), main ? 1 : 0, 1,
            new Dictionary<string, object?> { ["choiceCards"] = candidates.Select(item => new
                { id = item.card.Id.ToString(), number = item.card.Info.Number }).ToList() });
        if (answer.Count != 1) return;
        var selected = candidates.FirstOrDefault(item => item.card.Id.ToString() == answer[0]);
        if (selected.card is null || !ctx.State.Players[selected.owner].Characters.Contains(selected.card)
            || ctx.State.CurrentCostOf(selected.owner, selected.card) > limit) return;
        await AtomicOps.ProcessEffectLeavesAsync(ctx.State, selected.owner, [selected.card], ctx.Prompts,
            "bounce", AtomicOps.BounceToHand);
    }
}
