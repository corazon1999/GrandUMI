using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-050 密斯·奥利布（角色）【阻挡者】
/// 【登场时】可以将我方1张费用为2或更高的角色放回其持有者的手牌：抽取2张卡牌，丢弃我方的1张手牌。
///
/// 实现：可选成本（"可以…：…"）。我方场上无费用≥2角色则不可发动。ConfirmOptional 确认后
///   选1张费用≥2角色放回手牌(BounceToHand)，再抽2张、丢弃我方1张手牌。
/// </summary>
public class OP16_050_MissDoublefinger : IScriptedEffect
{
    public string CardNumber => "OP16-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var costCands = me.Characters.Where(c => c.Info.Cost >= 2).ToList();
        if (costCands.Count == 0) return; // 无费用≥2角色可回手 → 不可发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "密斯·奥利布【登场时】：将我方1张费用≥2角色放回手牌，抽2张并丢弃1张手牌？");
        if (!use) return;

        // 成本：选1张费用≥2角色放回手牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将1张费用≥2的角色放回手牌作为成本", costCands.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (pick.Count < 1) return;
        var bounce = costCands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounce);

        // 收益：抽2张，丢弃我方1张手牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        if (me.Hand.Count > 0)
        {
            var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃我方1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (dch.Count >= 1)
            {
                var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
                AtomicOps.DiscardHand(me, dcard);
            }
        }
    }
}
