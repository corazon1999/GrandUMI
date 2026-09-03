using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-045 蔡义。
/// 【阻挡者】
/// 【登场时】可以丢弃我方手牌中的1张事件：抽取2张卡牌。
/// </summary>
public sealed class OP15_045_Sai : IScriptedEffect
{
    public string CardNumber => "OP15-045";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var events = me.Hand.Where(card => card.Info.Kind == CardKind.Event).ToList();
        if (events.Count == 0) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃手牌中的1张事件，抽取2张卡牌？")) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events
                .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "OP15_045_DiscardEvent",
            "选择丢弃1张事件",
            events.Select(card => card.Id.ToString()).ToList(),
            1,
            1,
            extra);
        var discard = chosen.Count > 0
            ? events.FirstOrDefault(card => card.Id.ToString() == chosen[0])
            : null;
        if (discard is null) return;

        var previousPayingCost = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try
        {
            AtomicOps.DiscardHand(me, discard);
        }
        finally
        {
            EffectRuntime.PayingCost = previousPayingCost;
        }

        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
    }
}
