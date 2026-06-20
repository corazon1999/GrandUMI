using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-095 防护～～～～～～盾!（事件 / 地 / 德莱斯罗兹·巴尔托俱乐部）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，我方废弃区中有 15 张或更多卡牌的场合，本次战斗中，该卡牌的力量再 +2000。
/// 【触发】抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 反击(EventCounter)：先选 1 张我方领袖/角色 +2000(本次战斗)，若废弃区≥15 同一目标再 +2000。
///   - 触发(OnLifeRevealTrigger)：抽 2 张后让玩家弃 1 张手牌。
/// </summary>
public class OP04_095_GuardShield : IScriptedEffect
{
    public string CardNumber => "OP04-095";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择我方最多 1 张领袖或角色，本次战斗力量 +2000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
            if (me.Trash.Count >= 15)
                AtomicOps.AddPowerThisBattle(tgt, 2000);
            return;
        }

        // 【触发】抽 2 张，弃 1 张手牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        if (me.Hand.Count == 0) return;
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (disc.Count > 0)
        {
            var card = me.Hand.First(c => c.Id.ToString() == disc[0]);
            AtomicOps.DiscardHand(me, card);
        }
    }
}
