using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-047 希路麦波（角色 / 地 / 海军・利刃）
/// 【启动主要】可以将此角色放置到废弃区：将我方手牌或废弃区中最多 1 张"希路麦波"以外
///   费用不高于 3 且拥有《利刃》特征的角色卡牌登场。
///
/// 实现说明：
///   - 启动主要、可选（"可以…"）：成本为将自身放置废弃区（KO 到自身所属方废弃）。
///   - 候选跨手牌 + 废弃区：费用≤3、拥有《利刃》特征、卡名非"希路麦波"、为角色卡。
///   - 选中后按来源区分别登场：手牌用 PlayFromHandFree，废弃区用 PlayFromTrashFree。
///   - 自身先入废弃区，所以候选收集时排除自身（卡名已排除"希路麦波"，且自身将被 KO 移走）。
/// </summary>
public class EB04_047_Hibari : IScriptedEffect
{
    public string CardNumber => "EB04-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 收集候选：手牌 + 废弃区中费用≤3、《利刃》、角色、非"希路麦波"
        bool Match(CardInstance c) =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 3 &&
            c.Info.HasKeyword("利刃") &&
            !c.Info.NameContains("希路麦波");

        var handCands = me.Hand.Where(Match).ToList();
        var trashCands = me.Trash.Where(Match).ToList();
        if (handCands.Count == 0 && trashCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "希路麦波【启动主要】：将此角色放置废弃区，从手牌或废弃区登场 1 张费用≤3 的《利刃》角色？");
        if (!use) return;

        // 成本：将自身放置废弃区
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, self);

        // 重新收集（自身已离场，且废弃区里可能多了自身，但卡名为"希路麦波"已被排除）
        var allCands = new List<CardInstance>();
        allCands.AddRange(me.Hand.Where(Match));
        allCands.AddRange(me.Trash.Where(Match));
        if (allCands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = allCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HandOrTrashCharacter",
            "登场最多 1 张费用≤3 的《利刃》角色（手牌或废弃区）",
            allCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = allCands.First(c => c.Id.ToString() == chosen[0]);
        if (me.Hand.Contains(picked))
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        else if (me.Trash.Contains(picked))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
