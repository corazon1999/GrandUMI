using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-086 月光·莫利亚（角色）
/// 【登场时】选择我方废弃区中最多 1 张费用不高于 4 的角色卡牌和最多 1 张费用不高于 2 的
///   角色卡牌中的 1 张登场，剩余的卡牌以休息状态登场。
///
/// 实现说明 / 简化点：
///   - 分两个独立筛选槽：先从废弃区费用≤4 的角色里选最多 1 张，再从费用≤2 的角色里选最多 1 张
///     （两次选择互斥，已选的那张从第二槽候选里排除，避免同一张被选两次）。
///   - 选定的（最多 2 张）中，再选 1 张以正常（active）状态登场，其余以休息状态登场。
///   - 若只选了 1 张，则该张正常登场（无"剩余"可休息登场）。
/// </summary>
public class OP06_086_Moria : IScriptedEffect
{
    public string CardNumber => "OP06-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var picked = new List<CardInstance>();

        // 第一槽：费用≤4 的废弃区角色，最多 1 张
        var slot1 = me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 4).ToList();
        if (slot1.Count > 0)
        {
            var extra1 = new Dictionary<string, object?>
            {
                ["choiceCards"] = slot1.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
                "选择最多 1 张费用≤4 的废弃区角色", slot1.Select(c => c.Id.ToString()).ToList(), 0, 1, extra1);
            if (ch1.Count > 0) picked.Add(slot1.First(c => c.Id.ToString() == ch1[0]));
        }

        // 第二槽：费用≤2 的废弃区角色，最多 1 张（排除已选）
        var slot2 = me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 2 && !picked.Contains(c)).ToList();
        if (slot2.Count > 0)
        {
            var extra2 = new Dictionary<string, object?>
            {
                ["choiceCards"] = slot2.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
                "选择最多 1 张费用≤2 的废弃区角色", slot2.Select(c => c.Id.ToString()).ToList(), 0, 1, extra2);
            if (ch2.Count > 0) picked.Add(slot2.First(c => c.Id.ToString() == ch2[0]));
        }

        if (picked.Count == 0) return;

        // 从选定的卡中选 1 张正常登场，其余休息登场
        CardInstance activeOne;
        if (picked.Count == 1)
        {
            activeOne = picked[0];
        }
        else
        {
            var chA = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
                "选择 1 张以正常状态登场（其余以休息状态登场）",
                picked.Select(c => c.Id.ToString()).ToList(), 1, 1,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = picked.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                });
            activeOne = chA.Count > 0 ? picked.First(c => c.Id.ToString() == chA[0]) : picked[0];
        }

        foreach (var c in picked)
        {
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, c, restState: c != activeOne);
        }
    }
}
