using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-105 杰丽·邦妮（角色）
/// 【咚!!×1】【我方的回合中】【每回合1次】当对方的生命卡牌离开场上时，抽取2张卡牌，丢弃我方的1张手牌。
///
/// 实现说明 / 简化点：
///   - 用 Wave4 的 OnLifeLeaveField watcher，payload ctx.Vars["owner"]=失去生命的一方。
///     仅当离场的是对方生命牌（owner != ctx.OwnerIndex）时发动。
///   - 【咚!!×1】：此角色须被赋予 ≥1 张咚!!（AttachedDonCount(self.Id) >= 1）。
///   - 仅我方回合；每回合 1 次用 TurnOnceUsed 控制。
///   - 收益：抽 2 张，之后丢弃我方 1 张手牌（强制；手牌空则跳过）。
/// </summary>
public class OP08_105_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP08-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅我方的回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 离场的须为对方生命牌
        int owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner == ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】发动条件：此角色被赋予 ≥1 张咚!!
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-opplifedraw" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 之后：丢弃我方 1 张手牌（强制；手牌空则跳过）
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
