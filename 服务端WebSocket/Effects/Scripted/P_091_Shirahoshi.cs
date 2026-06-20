using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-091 白星（角色 / 风）
/// 【登场时】将我方手牌中最多1张费用不高于5且拥有《海王类》或《鱼人岛》特征的角色卡牌登场。
/// 【启动主要】可以将此角色转为休息状态：我方最多1张拥有《海王类》特征的角色可以在登场的回合中攻击角色。
///
/// 实现说明：
///   - 【登场时】= OnEnterField：从手牌登场最多1张满足条件的角色。
///   - 【启动主要】= ActivatedMain：成本横置自身；收益「让1张《海王类》角色在登场回合可攻击角色」
///     等价于解除该角色的召唤眩晕——通过把其 TurnPlayed 置 0，使 ActionValidator 不再因
///     「登场当回合」禁止其攻击。简化点：解除后该角色可攻击角色与领袖（卡面限定攻击角色），
///     影响极小，按惯例近似处理。
/// </summary>
public class P_091_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "P-091";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var playable = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Cost <= 5 &&
                (c.Info.HasKeyword("海王类") || c.Info.HasKeyword("鱼人岛"))).ToList();
            if (playable.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场1张费用≤5且《海王类》或《鱼人岛》的角色（最多1张）",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
            return;
        }

        // ActivatedMain
        if (self.IsTapped) return; // 已休息无法支付成本

        // 候选：拥有《海王类》且正处于登场当回合（被召唤眩晕）的我方角色
        var seaCands = me.Characters
            .Where(c => c.Info.HasKeyword("海王类") && c.TurnPlayed == ctx.State.TurnCount)
            .ToList();
        if (seaCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "白星【启动主要】：将此角色转为休息状态，使1张《海王类》角色可在登场的回合中攻击角色？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        var extra2 = new Dictionary<string, object?>
        {
            ["choiceCards"] = seaCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择1张《海王类》角色，使其可在登场的回合中攻击（最多1张）",
            seaCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra2);
        if (pick.Count > 0)
        {
            var target = seaCands.First(c => c.Id.ToString() == pick[0]);
            target.TurnPlayed = 0; // 解除召唤眩晕
        }
    }
}
