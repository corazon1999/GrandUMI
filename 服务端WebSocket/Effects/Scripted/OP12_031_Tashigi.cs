using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-031 达斯琪
/// 【登场时】将对方最多 1 张原本的费用不高于 6 的角色转为休息状态。
/// 之后，赋予我方领袖“罗罗诺亚·佐罗”最多 3 张休息状态的咚!!。
///
/// 说明 / 简化点：
///   - “最多 1 张”= 玩家可选 0~1 张（可跳过）。候选为对方原本费用 ≤6 的角色。
///   - “赋予领袖最多 3 张休息状态的咚” 仅在我方领袖为“罗罗诺亚·佐罗”时执行；
///     从费用区休息状态的咚中取最多 3 张赋予领袖（按现有休息咚数量自动赋予，不再二次询问数量）。
/// </summary>
public class OP12_031_Tashigi : IScriptedEffect
{
    public string CardNumber => "OP12-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 1. 将对方最多 1 张原本费用 ≤6 的角色转为休息状态（可选）
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        if (candidates.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张原本费用不高于 6 的角色转为休息状态",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (picked is not null) AtomicOps.RestCard(picked);
            }
        }

        // 2. 之后，赋予我方领袖“罗罗诺亚·佐罗”最多 3 张休息状态的咚!!
        if (me.Leader.MatchesName("罗罗诺亚·佐罗"))
        {
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 3, DonState.Rest);
        }
    }
}
