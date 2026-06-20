using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-033 布鲁诺（角色 / 暗 / 七水之城）
/// 【登场时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：我方领袖拥有《七水之城》特征的场合，
///   将我方手牌或废弃区中最多 1 张"布鲁诺"以外的费用为 5 且拥有《七水之城》特征的角色卡牌登场。
///
/// 实现说明：
///   - 可选成本【咚!!-1】：先 ConfirmOptional 确认；同意后 ReturnDonToDeck(me,1)。需费用区有可放回的咚!!。
///   - 仅领袖含《七水之城》时才有收益（否则发动只会白白咚-1），故仅在该前提下询问。
///   - 候选来自手牌 + 废弃区（跨双区合并），条件：非"布鲁诺"、原本费用为 5、含《七水之城》、角色卡。
///   - 按选中卡所在区域分别用 PlayFromHandFree / PlayFromTrashFree 登场。
/// </summary>
public class EB01_033_Blueno : IScriptedEffect
{
    public string CardNumber => "EB01-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅领袖含《七水之城》时本效果有收益
        if (!me.Leader.Info.HasKeyword("七水之城")) return;
        // 需费用区有可放回的咚!!（活跃/休息均可放回）
        if (me.TotalDonInCostArea < 1) return;

        bool Match(CardInstance c) =>
            c.Info.Kind == CardKind.Character &&
            !c.Info.NameContains("布鲁诺") &&
            c.Info.Cost == 5 &&
            c.Info.HasKeyword("七水之城");

        var fromHand = me.Hand.Where(Match).Select(c => (c, fromTrash: false)).ToList();
        var fromTrash = me.Trash.Where(Match).Select(c => (c, fromTrash: true)).ToList();
        var cands = new List<(CardInstance card, bool fromTrash)>();
        cands.AddRange(fromHand);
        cands.AddRange(fromTrash);
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "布鲁诺【登场时】：咚!!-1，将手牌或废弃区中 1 张费用 5 的《七水之城》角色登场？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HandOrTrashCharacter",
            "将手牌或废弃区中最多 1 张费用 5 的《七水之城》角色登场",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
        if (picked.fromTrash)
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked.card);
        else
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked.card);
    }
}
