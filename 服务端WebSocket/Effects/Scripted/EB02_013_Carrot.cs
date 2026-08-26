using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>EB02-013 凯罗特：检索并可从手牌登场舞台“佐乌”。</summary>
public sealed class EB02_013_Carrot : IScriptedEffect
{
    public string CardNumber => "EB02-013";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.CostArea.Count < 3) return;

        var top = me.Deck.Take(7).ToList();
        var candidates = top.Where(IsZou).ToList();
        if (top.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            };
            var pickedIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组最上方7张，公开最多1张“佐乌”加入手牌",
                candidates.Select(card => card.Id.ToString()).ToList(), 0, 1, extra);
            var picked = candidates.FirstOrDefault(card => pickedIds.Contains(card.Id.ToString()));

            foreach (var card in top) me.Deck.Remove(card);
            if (picked is not null)
            {
                me.Hand.Add(picked);
                top.Remove(picked);
                ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { picked.Info.Number });
            }

            var ordered = top;
            if (top.Count > 1)
            {
                var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
                    "将剩余卡牌自选顺序放回卡组最下方",
                    top.Select(card => card.Id.ToString()).ToList(), 0, top.Count, extra);
                var byId = top.ToDictionary(card => card.Id.ToString());
                ordered = order.Where(byId.ContainsKey).Select(id => byId[id]).Distinct().ToList();
                ordered.AddRange(top.Where(card => !ordered.Contains(card)));
            }
            me.Deck.AddRange(ordered);
        }

        var handStages = me.Hand.Where(IsZou).ToList();
        if (handStages.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandStage",
            "将手牌中最多1张“佐乌”登场",
            handStages.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = handStages.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var stage = handStages.FirstOrDefault(card => chosen.Contains(card.Id.ToString()));
        if (stage is not null) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, stage);
    }

    private static bool IsZou(CardInstance card)
        => card.Info.Kind == CardKind.Stage && card.Info.NameIs("佐乌");
}
