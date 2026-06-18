using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-015 莉莉康乃馨（角色，炎 4 费 0，FILM/狂欢岛）
/// 【启动主要】【每回合1次】可以将我方 1 张力量为 6000 或更高的角色放置到废弃区：
///   将我方废弃区中最多 1 张力量为 2000 到 5000 且拥有《FILM》特征的角色卡牌以休息状态登场。
///
/// 实现说明：
///   - 成本"将我方 1 张力量≥6000 的角色放置到废弃区"：以卡面原始力量 c.Info.Power 判定候选，
///     玩家选 1 张移入废弃区。DSL 的 cost 节无"按力量条件废弃指定己方角色"键，故用脚本。
///   - 收益：从废弃区登场最多 1 张力量 2000~5000 且 FILM 的角色（休息状态）。
///   - 可选效果先 ConfirmOptional；每回合 1 次用 TurnOnceUsed。
/// </summary>
public class OP06_015_LilyCarnation : IScriptedEffect
{
    public string CardNumber => "OP06-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：我方力量≥6000 的角色（卡面原始力量）
        var costCands = me.Characters.Where(c => c.Info.Power >= 6000).ToList();
        if (costCands.Count == 0) return;

        // 收益候选：废弃区中力量 2000~5000 且 FILM 的角色
        var reviveCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power >= 2000 && c.Info.Power <= 5000 &&
            c.Info.HasKeyword("FILM")).ToList();
        if (reviveCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莉莉康乃馨【启动主要】：废弃我方 1 张力量≥6000 的角色，从废弃区以休息状态登场 1 张力量 2000~5000 的 FILM 角色？");
        if (!use) return;

        // 成本：选择并废弃 1 张力量≥6000 的角色
        var chosenCost = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张力量≥6000 的角色放置到废弃区（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosenCost.Count == 0) return; // 未支付成本

        var costCard = costCands.First(c => c.Id.ToString() == chosenCost[0]);
        me.Characters.Remove(costCard);
        me.Trash.Add(costCard);

        me.TurnOnceUsed.Add(key);

        // 重新评估废弃区候选（成本卡可能不符，无需排除：其力量≥6000 不在区间内）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = reviveCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "从废弃区以休息状态登场最多 1 张力量 2000~5000 的 FILM 角色",
            reviveCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var revive = reviveCands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, revive, restState: true);
        }
    }
}
