using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-098 艾尼路（领航/领袖）
/// 【对方的回合中】【每回合1次】当我方生命区变为0张时，将我方卡组最上方的1张卡牌加入生命区最上方。
///   之后，丢弃我方的1张手牌。
///
/// 实现说明 / 简化点：
///   - 用 Wave4 的 OnLifeLeaveField watcher，payload ctx.Vars["owner"]=失去生命的一方，
///     ctx.Vars["toZero"]=本次离场后生命是否为 0 张。
///   - 仅对方的回合、且离场的是我方生命牌、且生命已变为 0 张时发动。
///   - 每回合 1 次：用 TurnOnceUsed 控制。
///   - "丢弃我方的1张手牌"为强制：让玩家选 1 张（手牌为空则跳过）。
/// </summary>
public class OP05_098_Enel : IScriptedEffect
{
    public string CardNumber => "OP05-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅对方的回合
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 离场的须为我方生命牌
        int owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return;

        // 须为"生命区变为 0 张"
        bool toZero = ctx.Vars.TryGetValue("toZero", out var zv) && zv is bool zb && zb;
        if (!toZero) return;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-life0" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 将卡组最上方 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 之后：丢弃我方 1 张手牌（手牌为空则跳过）
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count > 0)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }
    }
}
