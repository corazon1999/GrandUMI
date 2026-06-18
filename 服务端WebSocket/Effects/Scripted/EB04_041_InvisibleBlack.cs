using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-041 隐形·黑（事件 / 暗 / 草帽一伙 / cost3）
/// 【主要】我方领袖为"山智"，且我方场上存在4张或更多咚!!的场合，
///   将我方手牌或废弃区中最多1张力量不高于6000的"山智"登场。
/// 【触发】抽取2张卡牌，丢弃我方的1张手牌。（触发节由 DSL/wf 处理，本脚本只实现主要。）
///
/// 实现说明：
///   - 条件：领袖名为"山智"且我方费用区咚!!总数 ≥4。
///   - 候选取手牌与废弃区中名含"山智"、原本力量≤6000 的角色卡（跨两区），玩家最多选 1 张登场：
///     手牌来源用 PlayFromHandFree，废弃区来源用 PlayFromTrashFree。
/// </summary>
public class EB04_041_InvisibleBlack : IScriptedEffect
{
    public string CardNumber => "EB04-041";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：领袖为"山智" 且 我方咚!!≥4
        if (!me.Leader.Info.NameContains("山智")) return;
        if (me.TotalDonInCostArea < 4) return;

        bool MatchSanji(CardInstance c) =>
            c.Info.Kind == CardKind.Character && c.Info.NameContains("山智") && c.Info.Power <= 6000;

        var handCands = me.Hand.Where(MatchSanji).ToList();
        var trashCands = me.Trash.Where(MatchSanji).ToList();
        var all = handCands.Concat(trashCands).ToList();
        if (all.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayFromHandOrTrash",
            "将我方手牌或废弃区中最多 1 张力量≤6000 的「山智」登场",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = all.First(c => c.Id.ToString() == chosen[0]);
        if (me.Hand.Contains(picked))
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        else
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
