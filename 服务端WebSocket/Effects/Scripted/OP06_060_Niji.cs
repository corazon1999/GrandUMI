using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-060 温思默克·伊智（角色）
/// 【启动主要】咚!!-1，可以将此角色放置到废弃区：我方领袖拥有《GERMA 66》特征的场合，
///   将我方手牌或废弃区中最多 1 张费用为 7 的"温思默克·伊智"登场。
///
/// 实现说明 / 简化点：
///   - 成本为"咚!!-1 且将此角色放置到废弃区"，"可以"表示是否发动整个效果。
///   - 仅当领袖拥有《GERMA 66》特征且咚足够支付时才提示发动。
///   - 候选可来自手牌或废弃区，跨区域择一：脚本统一收集后让玩家选择，
///     再依其所在区域分别用 PlayFromHandFree / PlayFromTrashFree 登场。
/// </summary>
public class OP06_060_Niji : IScriptedEffect
{
    public string CardNumber => "OP06-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：领袖拥有《GERMA 66》特征
        if (!me.Leader.Info.HasKeyword("GERMA 66")) return;
        // 成本可支付性：至少 1 张可放回的咚
        if (me.CostArea.Count < 1) return;

        // 候选：手牌或废弃区中费用为 7 的"温思默克·伊智"
        var handCands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost == 7 && c.Info.Name.Contains("温思默克·伊智")).ToList();
        var trashCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost == 7 && c.Info.Name.Contains("温思默克·伊智")).ToList();
        if (handCands.Count == 0 && trashCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "温思默克·伊智【启动主要】：咚!!-1 并将此角色放置到废弃区，登场 1 张费用 7 的“温思默克·伊智”？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        // 成本：将此角色放置到废弃区
        me.Characters.Remove(self);
        me.Trash.Add(self);
        // 自身离场，清理其注册的持续效果（若有）
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == self.Id.ToString());

        // 收益：从手牌或废弃区择一登场
        var all = new List<CardInstance>();
        all.AddRange(handCands);
        all.AddRange(trashCands);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromHandOrTrash",
            "将手牌或废弃区中最多 1 张费用 7 的“温思默克·伊智”登场",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = all.First(c => c.Id.ToString() == chosen[0]);
            if (me.Hand.Contains(picked))
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            else if (me.Trash.Contains(picked))
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
