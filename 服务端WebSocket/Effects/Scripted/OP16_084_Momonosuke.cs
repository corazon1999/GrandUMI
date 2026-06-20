using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-084 光月桃之助（角色，地，和之国/光月家）
/// 【启动主要】可以将费用为 20 或更高的此角色放置到废弃区：我方场上存在 9 张或更多咚!! 的场合，
///            将我方废弃区中最多 1 张费用为 9 的"光月桃之助"登场。
///
/// 实现说明 / 简化点：
///   - 成本 = "此角色当前费用为 20 或更高时，将其放置到废弃区"。当前费用用 CurrentCostOf 评估
///     （需其他效果将此角色费用提升至 ≥20，否则无法支付成本）。
///   - 收益条件 = 我方场上(费用区)存在 9 张或更多咚!!。
///   - 目标 = 废弃区中卡名"光月桃之助"且卡面费用为 9 的角色，最多 1 张登场。
///   - 成本支付（自身入废弃区）在效果前执行，故目标候选会包含刚入废弃区的本卡（其卡面费用为 5，
///     不满足费用 9，自然被过滤排除）。
/// </summary>
public class OP16_084_Momonosuke : IScriptedEffect
{
    public string CardNumber => "OP16-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前置条件：此角色当前费用为 20 或更高
        if (ctx.State.CurrentCostOf(ctx.OwnerIndex, self) < 20) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "光月桃之助【启动主要】：将此角色放置到废弃区？（场上有 9 张以上咚!! 时，登场废弃区 1 张费用 9 的\"光月桃之助\"）");
        if (!use) return;

        // 支付成本：将此角色放置到废弃区
        me.Characters.Remove(self);
        me.Trash.Add(self);

        // 条件：我方场上存在 9 张或更多咚!!
        if (me.TotalDonInCostArea < 9) return;

        // 目标：废弃区中卡名"光月桃之助"且卡面费用为 9 的角色
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.MatchesName("光月桃之助") &&
            c.Info.Cost == 9).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区中最多 1 张费用 9 的\"光月桃之助\"登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
