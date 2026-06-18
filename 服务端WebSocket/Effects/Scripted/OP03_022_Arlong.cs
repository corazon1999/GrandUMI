using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-022 阿龙（领航 / 风·光 4 费 5000，鱼人族/东海/阿龙一伙）
/// 【咚!!×2】【攻击时】①（将费用区中1张咚!!转为休息状态）：
///   将我方手牌中最多1张费用不高于4且拥有【触发】效果的角色卡牌登场。
///
/// 实现说明：
///   - 发动条件【咚!!×2】：本领袖被赋予中的咚!! ≥2（AttachedDonCount）。
///   - 攻击时（OnAttackDeclare），可选成本=将费用区1张活跃咚!!转为休息状态。
///   - 收益：从手牌登场最多1张费用≤4且 Info.Trigger 非空（拥有【触发】效果）的角色。
/// </summary>
public class OP03_022_Arlong : IScriptedEffect
{
    public string CardNumber => "OP03-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 发动条件：本领袖被赋予中的咚!! ≥2
        if (me.AttachedDonCount(self.Id) < 2) return;

        // 成本前置：≥1张活跃咚!!
        var activeDons = me.CostArea
            .Where(d => d.State == DonState.Active && d.AttachedToCardId is null).ToList();
        if (activeDons.Count < 1) return;

        // 收益候选：手牌中费用≤4且拥有【触发】的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (playable.Count == 0) return;
        if (me.Characters.Count >= 5) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿龙【攻击时】：休息1张咚!!，从手牌登场最多1张费用≤4且拥有【触发】的角色？");
        if (!use) return;

        // 成本：休息1张活跃咚!!
        activeDons[0].State = DonState.Rest;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "从手牌登场最多1张费用≤4且拥有【触发】效果的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
