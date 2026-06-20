using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-045 克洛克达尔（角色）【阻挡者】
/// 【登场时】可以将我方1张费用为2或更高的角色放回其持有者的手牌：
///   将我方手牌中最多1张费用不高于2且拥有《因佩尔地狱》特征的角色卡牌登场。
///
/// 实现：可选成本（"可以…：…"）。我方场上无费用≥2角色则不可发动。ConfirmOptional 确认后
///   选1张费用≥2角色放回手牌(BounceToHand)，再登场手牌中≤1张费用≤2《因佩尔地狱》角色(PlayFromHandFree)。
/// </summary>
public class OP16_045_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP16-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var costCands = me.Characters.Where(c => c.Info.Cost >= 2).ToList();
        if (costCands.Count == 0) return; // 无费用≥2角色可回手 → 不可发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔【登场时】：将我方1张费用≥2角色放回手牌，登场手牌中≤1张费用≤2《因佩尔地狱》角色？");
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

        // 收益：登场手牌中最多1张费用≤2且《因佩尔地狱》角色
        var summonCands = me.Hand.Where(c => c.Info.Kind == CardKind.Character
            && c.Info.Cost <= 2 && c.Info.HasKeyword("因佩尔地狱")).ToList();
        if (summonCands.Count == 0) return;
        var sExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = summonCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var sPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromHand",
            "登场手牌中最多1张费用≤2《因佩尔地狱》角色", summonCands.Select(c => c.Id.ToString()).ToList(), 0, 1, sExtra);
        if (sPick.Count > 0)
        {
            var picked = summonCands.First(c => c.Id.ToString() == sPick[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
