using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>GERMA 变身先支付返回咚和自弃费用，之后选择最多一张登场目标。</summary>
internal static class GermaTransformationEffect
{
    public static async Task Resolve(EffectContext ctx, string name, int cost)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        if (!me.Characters.Contains(self) || me.CostArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"{name}【启动主要】：咚!!-1并将此角色放置废弃区，之后可登场最多1张费用{cost}的同名角色？")) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        // 自弃是费用，不是KO；统一角色区离场回调负责清理场上状态。
        if (!me.Characters.Remove(self)) return;
        me.Trash.Add(self);
        if (!me.Leader.Info.HasKeyword("GERMA 66")) return;

        bool Matches(CardInstance card) => card.Info.Kind == CardKind.Character
            && card.Info.Cost == cost && card.MatchesName(name);
        var candidates = me.Hand.Concat(me.Trash).Where(Matches).DistinctBy(card => card.Id).ToList();
        if (candidates.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayFromHandOrTrash",
            $"选择最多1张费用{cost}的“{name}”登场（手牌或废弃区，可以选择0张）",
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var target = candidates.FirstOrDefault(card => chosen.Count == 1 && card.Id.ToString() == chosen[0]);
        if (target is null || !Matches(target)) return;
        if (me.Hand.Contains(target)) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, target);
        else if (me.Trash.Contains(target)) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, target);
    }
}
