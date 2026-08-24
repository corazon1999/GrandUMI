using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>EB01-009「少啰唆！！！跟我走吧！！！」。</summary>
public sealed class EB01_009_ComeWithMe : IScriptedEffect
{
    public string CardNumber => "EB01-009";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(Math.Min(5, me.Deck.Count)).ToList();
        if (top.Count == 0) return;

        var candidates = top.Where(card => card.Info.Kind == CardKind.Character
            && card.Info.Cost <= 3 && card.Info.HasKeyword("动物")).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromDeck",
            $"确认卡组顶 {top.Count} 张，将最多1张费用不高于3的《动物》角色登场",
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });

        if (chosen.Count > 0)
        {
            var picked = candidates.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
            if (picked is not null)
            {
                ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { picked.Info.Number });
                await AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        var rest = top.Where(me.Deck.Contains).ToList();
        foreach (var card in rest) me.Deck.Remove(card);
        if (rest.Count <= 1)
        {
            me.Deck.AddRange(rest);
            return;
        }

        var orderedIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
            "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
            rest.Select(card => card.Id.ToString()).ToList(), 0, rest.Count,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = rest.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                ["allowDefaultOrder"] = true,
            });
        var ordered = orderedIds
            .Select(id => rest.FirstOrDefault(card => card.Id.ToString() == id))
            .Where(card => card is not null).Cast<CardInstance>().Distinct().ToList();
        ordered.AddRange(rest.Where(card => !ordered.Contains(card)));
        me.Deck.AddRange(ordered);
    }
}

/// <summary>ST30-014 Mr.3：一次选取互不重复的最多2张角色，各赋予最多2张休息咚。</summary>
public sealed class ST30_014_Mr3 : IScriptedEffect
{
    public string CardNumber => "ST30-014";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Source.IsTapped) return;
        AtomicOps.RestCard(ctx.Source);
        if (!ctx.Source.IsTapped) return;

        var candidates = me.Characters.Where(card => card.Info.Power == 6000).ToList();
        if (candidates.Count == 0 || me.RestDonCount == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多2张原本力量6000的角色，各赋予最多2张休息咚",
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 2,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        foreach (var id in chosen.Distinct())
        {
            var target = candidates.FirstOrDefault(card => card.Id.ToString() == id);
            int max = Math.Min(2, me.RestDonCount);
            if (target is null || max == 0) continue;
            int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                $"选择赋予「{target.Info.Name}」的休息咚!!数量",
                Enumerable.Range(0, max + 1).Select(n => $"{n} 张").ToList());
            int count = Math.Clamp(option, 0, max);
            AtomicOps.AttachDonFromCost(me, target.Id, count, DonState.Rest);
        }
    }
}
