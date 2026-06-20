using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-069 汤姆（2 费 2000，鱼人族/七水之城）
/// 【登场时】咚!!-1：将我方废弃区中最多 1 张费用不高于 3 的舞台卡牌加入手牌。
///
/// 实现：登场时触发。代价咚!!-1（将 1 张活跃咚放回咚卡组）。
/// 候选 = 我方废弃区中所有费用不高于 3 的舞台卡牌；可选取最多 1 张加入手牌（可不选）。
/// 由于"可以"语义体现为最多 1 张（min=0），且需先支付代价，故先确认是否发动。
/// 废弃区为客户端默认不可见区域，故下发 extra.choiceCards 以显示卡面。
/// </summary>
public class OP13_069_Tom : IScriptedEffect
{
    public string CardNumber => "OP13-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：废弃区中费用不高于 3 的舞台卡牌
        var candidates = me.Trash
            .Where(c => c.Info.Kind == CardKind.Stage && c.Info.Cost <= 3)
            .ToList();
        if (candidates.Count == 0) return;

        // 代价：咚!!-1，需至少 1 张可放回的咚
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "汤姆：咚!!-1，将废弃区中最多 1 张费用不高于 3 的舞台卡牌加入手牌？");
        if (!use) return;

        // 支付代价：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashStage",
            "将废弃区中最多 1 张费用不高于 3 的舞台卡牌加入手牌",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (picked is not null) AtomicOps.TrashToHand(me, picked);
    }
}
