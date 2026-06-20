using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-026 古伊娜
/// 【启动主要】可以将此角色转为休息状态：
///   将对方最多 1 张原本的费用不高于 4 的角色转为休息状态。
///   之后，赋予我方领袖"罗罗诺亚·佐罗"最多 3 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 成本"将此角色转为休息状态"：自身已休息则无法发动。
///   - "费用不高于 4"按原本费用(Info.Cost)判定。
///   - "赋予休息状态的咚"：从费用区取处于休息状态的咚附给领袖（与 DSL AttachDon from=rest 一致）。
///     仅当我方领袖为"罗罗诺亚·佐罗"时才有赋予对象；否则该步跳过。
///   - 取多少咚由可用的休息咚数量决定，最多 3 张（无上限选择 UI，自动取最多 3 张）。
/// </summary>
public class OP12_026_Kuina : IScriptedEffect
{
    public string CardNumber => "OP12-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本：自身转休息（已休息无法发动）
        if (ctx.Source.IsTapped) return;
        ctx.Source.IsTapped = true;

        // 1. 将对方最多 1 张原本费用不高于 4 的角色转为休息状态
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (candidates.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张原本的费用不高于 4 的角色转为休息状态",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = candidates.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(target);
            }
        }

        // 2. 赋予我方领袖"罗罗诺亚·佐罗"最多 3 张休息状态的咚!!
        if (me.Leader.MatchesName("罗罗诺亚·佐罗"))
        {
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 3, DonState.Rest);
        }
    }
}
