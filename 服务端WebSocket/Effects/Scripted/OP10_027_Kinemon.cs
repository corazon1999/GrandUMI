using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-027 锦卫门（角色，力量 0）
/// 【启动主要】可以将此角色和我方废弃区中 1 张力量为 1000 的"锦卫门"自选顺序放回卡组最下方：
///   将我方手牌中最多 1 张费用为 6 的"锦卫门"登场。
///
/// 实现说明：
///   - 成本：将此角色 + 废弃区 1 张力量 1000 的"锦卫门"放回卡组最下方。
///     需废弃区存在符合条件的"锦卫门"，否则无法支付成本，不发动。
///   - "自选顺序放回卡组最下方"实现为依次放底（对实战影响极小）。
///   - 收益：从手牌登场最多 1 张费用 6 的"锦卫门"。
/// </summary>
public class OP10_027_Kinemon : IScriptedEffect
{
    public string CardNumber => "OP10-027";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前置：废弃区需有力量 1000 的"锦卫门"
        var trashKinemon = me.Trash
            .Where(c => c.Info.Power == 1000 && c.MatchesName("锦卫门"))
            .ToList();
        if (trashKinemon.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "锦卫门【启动主要】：将此角色和废弃区 1 张力量 1000 的\"锦卫门\"放回卡组底，登场手牌中 1 张费用 6 的\"锦卫门\"？");
        if (!use) return;

        // 选择废弃区中作为成本的 1 张力量 1000 的"锦卫门"
        var extraTrash = new Dictionary<string, object?>
        {
            ["choiceCards"] = trashKinemon.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pickTrash = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCharacter",
            "选择废弃区 1 张力量 1000 的\"锦卫门\"放回卡组底（成本）",
            trashKinemon.Select(c => c.Id.ToString()).ToList(), 1, 1, extraTrash);
        if (pickTrash.Count < 1) return; // 成本未支付

        // 支付成本：废弃区那张放底 + 自身放底
        var trashCost = trashKinemon.First(c => c.Id.ToString() == pickTrash[0]);
        AtomicOps.ReturnTrashToDeckBottom(me, trashCost);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, self);

        // 收益：从手牌登场最多 1 张费用 6 的"锦卫门"
        var playable = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 6 && c.MatchesName("锦卫门"))
            .ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场手牌中最多 1 张费用 6 的\"锦卫门\"",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
