using System.Linq;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST12-001（角色）
/// 【咚!!×1】【攻击时】【每回合1次】可以将我方1张费用为2或更高的角色放回其持有者的手牌：
///   将我方最多1张力量不高于7000的角色转为活跃状态。
///
/// 实现:缺条目(攻击时效果原先完全未实现,反馈#127审计发现)。【咚×1】门槛脚本自检+每回合1次;
///   可选成本=放回我方1张≥2费角色到手牌→收益=活跃我方1张力量≤7000的休息角色。
/// </summary>
public class ST12_001_AttackDon : IScriptedEffect
{
    public string CardNumber => "ST12-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 【咚!!×1】
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
        // 【每回合1次】
        var key = "ST12-001-atk:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 可选成本:将我方1张费用≥2角色放回手牌
        var costCands = me.Characters.Where(c => c.Info.Cost >= 2).ToList();
        if (costCands.Count == 0) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "ST12-001【攻击时】:将我方1张费用≥2的角色放回手牌,使我方最多1张力量≤7000的角色转为活跃?");
        if (!use) return;
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var paid = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择1张费用≥2的我方角色放回手牌(成本)", costCands.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (paid.Count == 0) return;
        var costTarget = costCands.First(c => c.Id.ToString() == paid[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, costTarget);
        me.TurnOnceUsed.Add(key);

        // 收益:我方最多1张力量≤7000的休息角色转为活跃
        var actCands = me.Characters.Where(c => c.IsTapped && ctx.State.CurrentPowerOf(ctx.OwnerIndex, c) <= 7000).ToList();
        if (actCands.Count == 0) return;
        var actExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = actCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多1张力量≤7000的角色转为活跃", actCands.Select(c => c.Id.ToString()).ToList(), 0, 1, actExtra);
        if (pick.Count > 0)
            AtomicOps.ActivateCard(actCands.First(c => c.Id.ToString() == pick[0]));
    }
}
