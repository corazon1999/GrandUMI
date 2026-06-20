using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-079 伊姆（领航）
/// 【启动主要】【每回合1次】可以将我方1张拥有《天龙人》特征的角色或我方1张手牌放置到废弃区：抽取1张卡牌。
///
/// 实现说明 / 简化点：
///   - 卡面另含两项无法表达的规则级机制（构筑上不能放2费以上事件、游戏开始时登场圣地玛丽乔尔舞台），
///     这两项引擎无通道，本脚本仅实现可脚本化的【启动主要】抽牌效果。
///   - 成本为二选一：弃 1 张《天龙人》角色 或 弃 1 张手牌，支付成本后抽 1 张。
///   - "可以"=可选，先 ConfirmOptional。
/// </summary>
public class OP13_079_Imu : IScriptedEffect
{
    public string CardNumber => "OP13-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合1次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 可支付的成本来源
        var tenryubito = me.Characters.Where(c => c.Info.HasKeyword("天龙人")).ToList();
        bool canTrashChar = tenryubito.Count > 0;
        bool canTrashHand = me.Hand.Count > 0;
        if (!canTrashChar && !canTrashHand) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "伊姆【启动主要】：弃置 1 张《天龙人》角色或 1 张手牌，抽 1 张卡牌？");
        if (!use) return;

        // 二选一：弃天龙人角色 / 弃手牌
        int branch;
        if (canTrashChar && canTrashHand)
        {
            branch = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择支付的成本",
                new[] { "弃置 1 张《天龙人》角色", "弃置 1 张手牌" });
        }
        else
        {
            branch = canTrashChar ? 0 : 1;
        }

        if (branch == 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择 1 张《天龙人》角色放置到废弃区",
                tenryubito.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) return;
            var tgt = tenryubito.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, ctx.OwnerIndex, tgt);
        }
        else
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "选择 1 张手牌放置到废弃区",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            if (chosen.Count == 0) return;
            var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.DiscardHand(me, card);
        }

        // 成本已支付 → 抽 1
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        me.TurnOnceUsed.Add(key);
    }
}
