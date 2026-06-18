using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-097 我这是彻底退步了啊……!!!（事件，地）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +1000。
///   之后，我方废弃区中有 10 张或更多卡牌的场合，
///   将我方废弃区中最多 1 张费用不高于 3 的黑色角色卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 反击 +1000 后的“废弃区≥10 时检索”为脚本内条件分支。
///   - “黑色”按 ColorList 含“暗”判定（黑色元素色）。
/// </summary>
public class OP11_097_TotallyRegressed : IScriptedEffect
{
    public string CardNumber => "OP11-097";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色力量 +1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked.Count > 0)
        {
            var tgt = targets.FirstOrDefault(c => c.Id.ToString() == picked[0]);
            if (tgt is not null) AtomicOps.AddPowerThisBattle(tgt, 1000);
        }

        // 之后：废弃区≥10 张时，将最多 1 张费用≤3 的黑色角色加入手牌
        if (me.Trash.Count < 10) return;

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 3 &&
            c.Info.ColorList.Contains("紫")).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "将废弃区中最多 1 张费用≤3 的黑色角色加入手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var card = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (card is not null) AtomicOps.TrashToHand(me, card);
        }
    }
}
