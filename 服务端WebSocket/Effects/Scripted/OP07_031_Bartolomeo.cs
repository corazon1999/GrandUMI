using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-031 巴尔托洛梅奥（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【我方的回合中】【每回合1次】当角色因我方的效果转为休息状态时，
///   抽取1张卡牌，丢弃我方的1张手牌。
///
/// 实现说明：
/// - 用 Wave2 反应式 watcher OnCharRested 监听"角色因效果转为休息状态时"。
/// - 仅我方回合内有效；每回合1次。任意角色被横置都触发（不限本卡）。
/// - 先抽 1，再让玩家从手牌选 1 张丢弃。
/// - 【阻挡者】为纯关键词，由引擎处理，此处仅实现主动效果部分。
/// </summary>
public class OP07_031_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "OP07-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-rested" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 抽取1张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 丢弃我方的1张手牌
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count > 0)
        {
            var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.DiscardHand(me, card);
        }
    }
}
