using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-050 波尔·桑达索尼娅（角色）
/// 【登场时】我方场上存在2张或更多拥有《亚马逊·百合》或《九蛇海盗团》特征的角色的场合，
///   将对方最多1张费用不高于3的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 登场条件按特征过滤计数（含此卡自身，登场时已在 Characters 内）。
///   - 满足条件后，将对方最多 1 张费用≤3 的角色退回手牌。
/// </summary>
public class OP07_050_Boa_Sandersonia : IScriptedEffect
{
    public string CardNumber => "OP07-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int featured = me.Characters.Count(c =>
            c.Info.HasKeyword("亚马逊·百合") || c.Info.HasKeyword("九蛇海盗团"));
        if (featured < 2) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色放回其持有者手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, picked);
    }
}
