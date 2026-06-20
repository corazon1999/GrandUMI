using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-025 克洛（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】赋予对方 1 张角色最多 2 张对方费用区中的咚!!。
///   之后，当本回合结束时，对方最多 1 张被赋予 3 张或更多咚!! 且处于休息状态的角色，
///   在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - "赋予对方 1 张角色最多 2 张对方费用区中的咚" = 从对方费用区取最多 2 张活跃咚附到对方所选角色。
///     用 AtomicOps.AttachDonFromCost(opp, target.Id, n, DonState.Active)。
///   - 第二段"当本回合结束时…不会转为活跃状态"原文为延迟到回合结束触发；本引擎在【登场时】
///     时机内一并结算（PreventActivateNextReset 标记会保留到对方下个重置阶段，效果等价）。
///     选择对象时即时判定"被赋予 3 张或更多咚且休息状态"，故先附咚再判定。
/// </summary>
public class OP15_025_Kuro : IScriptedEffect
{
    public string CardNumber => "OP15-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        // ── 赋予对方 1 张角色最多 2 张对方费用区中的咚!! ──
        if (opp.Characters.Count > 0)
        {
            var targets = opp.Characters;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方 1 张角色，赋予其最多 2 张对方费用区中的咚!!",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
            {
                var tgt = targets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(opp, tgt.Id, 2, DonState.Active);
            }
        }

        // ── 之后：对方最多 1 张被赋予 3 张或更多咚且处于休息状态的角色，下个重置阶段不会转为活跃 ──
        var lockable = opp.Characters
            .Where(c => c.IsTapped && opp.AttachedDonCount(c.Id) >= 3)
            .ToList();
        if (lockable.Count > 0)
        {
            var extra2 = new Dictionary<string, object?>
            {
                ["choiceCards"] = lockable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张被赋予≥3张咚且休息状态的角色，使其在下个重置阶段不转为活跃",
                lockable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra2);
            if (pick2.Count > 0)
            {
                var locked = lockable.First(c => c.Id.ToString() == pick2[0]);
                AtomicOps.PreventActivateNextReset(locked);
            }
        }
    }
}
