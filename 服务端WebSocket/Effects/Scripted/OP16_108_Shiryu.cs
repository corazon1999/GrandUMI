using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-108 希流（角色 / cost6）
/// 【登场时】可以丢弃我方的1张手牌：将我方废弃区中最多1张费用不高于6且拥有《黑胡子海盗团》特征的
///   卡牌正面朝上加入生命区最上方。
///
/// 旧 DSL 占位错写成【触发】抽2，与原文不符，改脚本。可选成本（"可以…：…"），手牌为空则不可发动。
///   ConfirmOptional 确认后丢1手牌，再从废弃区选≤1张费用≤6《黑胡子海盗团》卡牌加入生命区最上方。
///   （"卡牌"不限角色；生命卡入区即正面朝上，引擎生命不分正反故直接入顶。）
/// </summary>
public class OP16_108_Shiryu : IScriptedEffect
{
    public string CardNumber => "OP16-108";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return; // 无手牌可丢 → 不可发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "希流【登场时】：丢弃1张手牌，将废弃区1张费用≤6《黑胡子海盗团》卡牌正面朝上加入生命区最上方？");
        if (!use) return;

        // 成本：丢1手牌
        var dExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃1张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, dExtra);
        if (dch.Count < 1) return;
        var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 收益：废弃区选≤1张费用≤6《黑胡子海盗团》卡牌加入生命区最上方
        var cands = me.Trash.Where(c => c.Info.Cost <= 6 && c.Info.HasKeyword("黑胡子海盗团")).ToList();
        if (cands.Count == 0) return;
        var tExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var tch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "选废弃区1张费用≤6《黑胡子海盗团》卡牌加入生命区最上方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, tExtra);
        if (tch.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == tch[0]);
            me.Trash.Remove(picked);
            me.LifeArea.Insert(0, picked); // 加入生命区最上方
        }
    }
}
