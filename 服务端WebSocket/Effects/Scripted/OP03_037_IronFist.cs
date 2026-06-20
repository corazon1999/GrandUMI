using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-037 铁齿功（事件）
/// 【主要】可以将我方1张拥有《东海》特征的角色转为休息状态：
///   将对方最多1张费用不高于3且处于休息状态的角色KO。
/// 【触发】将我方手牌中最多1张费用不高于4且拥有【触发】效果的角色卡牌登场。
///
/// 实现说明：
///   - 主要：成本"横置我方1张《东海》角色"用 ConfirmOptional + ChooseCards 选目标横置，再KO对方目标。
///   - 触发：从手牌登场费用≤4且 Info.Trigger 非空的角色（"拥有【触发】效果"=Trigger 非空）。
/// </summary>
public class OP03_037_IronFist : IScriptedEffect
{
    public string CardNumber => "OP03-037";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // KO 目标：对方费用≤3且休息状态
            var koTargets = opp.Characters
                .Where(c => c.IsTapped && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3)
                .ToList();
            // 成本目标：我方《东海》且活跃的角色
            var costCands = me.Characters.Where(c => c.Info.HasKeyword("东海") && !c.IsTapped).ToList();
            if (koTargets.Count == 0 || costCands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "铁齿功【主要】：将我方1张《东海》角色转为休息状态，将对方最多1张费用≤3且休息的角色KO？");
            if (!use) return;

            // 成本：横置我方1张《东海》角色
            var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "将我方1张《东海》角色转为休息状态（成本）",
                costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (costPick.Count < 1) return;
            AtomicOps.RestCard(costCands.First(c => c.Id.ToString() == costPick[0]));

            // 效果：KO 对方最多1张
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤3且处于休息状态的角色KO",
                koTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = koTargets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else // OnLifeRevealTrigger
        {
            if (me.Characters.Count >= 5) return;
            var playable = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Cost <= 4 &&
                !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
            if (playable.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "将我方手牌中最多1张费用≤4且拥有【触发】效果的角色登场",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }
    }
}
