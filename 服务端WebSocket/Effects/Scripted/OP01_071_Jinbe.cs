using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-071 甚平（角色 / 水 / 鱼人族·草帽一伙）
/// 【登场时】将最多1张费用不高于3的角色放置到其持有者卡组的最下方。
/// 【触发】此卡牌登场。
///
/// 实现说明：
///   - 【登场时】(OnEnterField)：合并双方场上角色（费用≤3）为候选，选中后放回其持有者卡组最下方。
///   - 【触发】(OnLifeRevealTrigger)：此卡作为被翻开的生命牌已被放入废弃区，将其从废弃区登场。
/// </summary>
public class OP01_071_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP01-071";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 此卡牌登场：从废弃区登场到我方场上
            if (me.Trash.Contains(ctx.Source))
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
            return;
        }

        // 【登场时】将最多1张费用≤3的角色（任意一方）放回其持有者卡组最下方
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters)
            if (ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 3) cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters)
            if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3) cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多1张费用≤3的角色放到其持有者卡组最下方",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.owner, picked.card);
        }
    }
}
