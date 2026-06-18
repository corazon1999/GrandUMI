using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-094 空气开门（事件 / 地 / CP9）
/// 【主要】我方领袖拥有的特征中包含&lt;CP&gt;的场合，确认我方卡组最上方的 5 张卡牌，
///   将最多 1 张费用不高于 5 且拥有的特征中包含&lt;CP&gt;的角色卡牌登场。
///   之后，将剩余的卡牌放置到废弃区。
/// 【触发】将我方废弃区中最多 1 张费用不高于 3 的黑色角色卡牌登场。
///
/// 实现说明：
///   - 主要段(EventMain)：领袖含 CP 时看顶 5 张，候选为费用≤5 且含 CP 特征的角色；
///     选 1 张直接登场（先从卡组移入手牌再 PlayFromHandFree 实现"从卡组登场"），剩余入废弃。
///   - 触发段(OnLifeRevealTrigger)：从废弃区登场最多 1 张费用≤3 的黑色（地）角色。
/// </summary>
public class OP03_094_AirDoor : IScriptedEffect
{
    public string CardNumber => "OP03-094";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】从废弃区登场最多 1 张费用≤3 的黑色角色
            var trashCands = me.Trash.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Cost <= 3 &&
                c.Info.ColorList.Contains("黑")).ToList();
            if (trashCands.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = trashCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
                "从废弃区登场最多 1 张费用≤3 的黑色角色",
                trashCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
            {
                var picked = trashCands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.PlayFromTrashFree(ctx.State, owner, picked);
            }
            return;
        }

        // 【主要】领袖须含 CP 特征
        if (!me.Leader.Info.Keywords.Any(k => k.Contains("CP"))) return;

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            c.Info.Keywords.Any(k => k.Contains("CP"))).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopPlay",
                "将最多 1 张费用≤5 且含<CP>特征的角色登场",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                // 从卡组登场：移入手牌后免费登场
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
                AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
            }
        }

        // 之后：剩余仍在顶部的卡牌全部放置到废弃区
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest)
        {
            me.Deck.Remove(c);
            me.Trash.Add(c);
        }
    }
}
