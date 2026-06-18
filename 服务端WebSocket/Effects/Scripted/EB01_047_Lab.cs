using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-047 拉布（角色 / 地 / 动物，cost2 power4000）
/// 【每回合1次】当角色被KO时，抽取 1 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明（M5 任意角色被KO watcher）：
///   - 订阅 OnAnyCharKOd（战斗/效果KO 均派发；卡数据 effectTags 已加该标签以被 CollectListeners 收集）。
///   - 监听任意一方角色被KO（含对方角色、我方其它角色；本卡自身被KO时已在废弃区不会被收集）。
///   - 【每回合1次】用 TurnOnceUsed 去重（回合结束清空）。抽 1 张后由玩家自选弃 1 张手牌。
/// </summary>
public class EB01_047_Lab : IScriptedEffect
{
    public string CardNumber => "EB01-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        if (me.Hand.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        var card = chosen.Count > 0 ? me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]) : me.Hand[0];
        if (card is not null) AtomicOps.DiscardHand(me, card);
    }
}
