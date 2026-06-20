using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-070 大面包（角色）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，将我方手牌中最多 1 张
///   费用不高于 4 且拥有《福克斯海盗团》特征的卡牌登场。
///
/// 实现说明 / 简化点：
///   - 双方咚!! 张数相对比较用 me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 候选取手牌中费用≤4 且《福克斯海盗团》的卡，登场用 PlayFromHandFree。
///   - 费用按 CurrentCost() 评估。
/// </summary>
public class OP07_070_BigPan : IScriptedEffect
{
    public string CardNumber => "OP07-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        var playable = me.Hand
            .Where(c => ctx.State.CurrentCostOf(c) <= 4 && c.Info.HasKeyword("福克斯海盗团"))
            .ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用≤4 的《福克斯海盗团》卡牌登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
