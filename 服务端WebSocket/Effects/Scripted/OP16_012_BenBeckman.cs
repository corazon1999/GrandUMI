using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-012 本·贝克曼（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】可以将我方的1张咚!!转为休息状态：我方领袖拥有《红发海盗团》特征，
///   且我方场上存在10张咚!!的场合，将我方手牌中最多1张"杰克斯"登场。
///
/// 实现说明 / 简化点：
///   - 可选成本"将1张咚转为休息状态"在触发节无对应通道，按惯例只实现收益部分（成本省略），
///     收益本身要求"场上存在10张咚!!"，已用费用区咚总数判定。
///   - 条件：领袖具有《红发海盗团》特征 + 我方费用区咚总数 ≥ 10。
/// </summary>
public class OP16_012_BenBeckman : IScriptedEffect
{
    public string CardNumber => "OP16-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：领袖拥有《红发海盗团》特征
        if (!me.Leader.Info.HasKeyword("红发海盗团")) return;
        // 条件：我方场上存在 10 张或更多咚!!
        if (me.TotalDonInCostArea < 10) return;

        // 收益：将手牌中最多1张"杰克斯"登场
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.MatchesName("杰克斯")).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多1张\"杰克斯\"登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
