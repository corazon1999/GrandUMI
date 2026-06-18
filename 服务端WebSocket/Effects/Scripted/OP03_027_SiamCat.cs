using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-027 暹罗猫（角色）
/// 【登场时】我方领袖拥有《东海》特征的场合：
///   将对方最多1张费用不高于2的角色转为休息状态；
///   我方场上不存在"花斑猫"的场合，将我方手牌中最多1张"花斑猫"登场。
///
/// 实现说明：
///   - 领袖《东海》前提用关键词判断。"我方场上不存在花斑猫"用 Characters.Any(MatchesName) 判定。
///   - 休息对方角色用 AtomicOps.RestCard；从手牌登场用 PlayFromHandFree。
/// </summary>
public class OP03_027_SiamCat : IScriptedEffect
{
    public string CardNumber => "OP03-027";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 前提：我方领袖拥有《东海》特征
        if (!me.Leader.Info.HasKeyword("东海")) return;

        // 效果1：将对方最多1张费用≤2的角色转为休息状态
        var restTargets = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 2)
            .ToList();
        if (restTargets.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤2的角色转为休息状态",
                restTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = restTargets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }

        // 效果2：我方场上不存在"花斑猫"时，将手牌中最多1张"花斑猫"登场
        bool hasOnField = me.Characters.Any(c => c.MatchesName("花斑猫"));
        if (hasOnField) return;
        if (me.Characters.Count >= 5) return;

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.MatchesName("花斑猫")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将我方手牌中最多1张\"花斑猫\"登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
