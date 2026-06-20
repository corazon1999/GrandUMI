using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-066 温思默克·勇智（费用2 角色）
/// 【启动主要】咚!!-1（可以将我方场上指定数量的咚‼放回咚‼卡组），可以将此角色放置到废弃区：
/// 我方领袖拥有《GERMA 66》特征的场合，将我方手牌或废弃区中最多 1 张费用为 4 的"温思默克·勇智"登场。
///
/// 实现说明：同 OP06-064，区别为目标名为"温思默克·勇智"且费用为 4。
/// </summary>
public class OP06_066_Yonji : IScriptedEffect
{
    public string CardNumber => "OP06-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (!me.Leader.Info.HasKeyword("GERMA 66")) return;

        var handCands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 4 &&
            c.MatchesName("温思默克·勇智")).ToList();
        var trashCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 4 &&
            c.MatchesName("温思默克·勇智")).ToList();
        var all = new List<CardInstance>();
        all.AddRange(handCands);
        all.AddRange(trashCands);
        if (all.Count == 0) return;

        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "勇智【启动主要】：咚!!-1 并将此角色放置废弃区，从手牌/废弃区登场 1 张费用4的“温思默克·勇智”？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayFromHandOrTrash",
            "选择 1 张费用4的“温思默克·勇智”登场（手牌或废弃区）",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        // 「放置到废弃区」是自费而非 KO：手动移入废弃区，避免被 KO 守护拦截或误触发对方「角色被KO时」反应
        me.Characters.Remove(self);
        me.Trash.Add(self);
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == self.Id.ToString());

        var picked = all.First(c => c.Id.ToString() == chosen[0]);
        if (me.Hand.Contains(picked))
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        else
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
