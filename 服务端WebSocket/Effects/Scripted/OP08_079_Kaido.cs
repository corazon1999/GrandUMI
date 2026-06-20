using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-079 盖德（角色 / 地 / 9 费 / 9000 / 原洛克斯海盗团·百兽海盗团）
/// 【启动主要】【每回合1次】可以丢弃我方的 1 张手牌：在此角色登场的回合的场合，
///   将对方最多 1 张费用不高于 7 的角色放置到废弃区（KO）。之后，对方丢弃其 1 张手牌。
///
/// 说明 / 简化点：
///   - 【每回合1次】用 me.TurnOnceUsed 去重。
///   - "此角色登场的回合"用 self.TurnPlayed == ctx.State.TurnCount 判定。
///   - "可以丢弃手牌"为可选成本：ConfirmOptional 询问后选 1 张手牌丢弃。
///   - "对方丢弃其 1 张手牌"用 AtomicOps.OpponentDiscardChosen（需 ctx.Engine）。
/// </summary>
public class OP08_079_Kaido : IScriptedEffect
{
    public string CardNumber => "OP08-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【每回合1次】
        var key = self.Info.Number + "-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：在此角色登场的回合
        if (self.TurnPlayed != ctx.State.TurnCount) return;

        // 需要有手牌可作为成本
        if (me.Hand.Count == 0) return;

        // 可选成本：丢弃我方 1 张手牌
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "盖德【启动主要】：丢弃我方 1 张手牌，将对方最多 1 张费用≤7 的角色 KO，之后对方丢 1 张手牌？");
        if (!use) return;

        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardPick.Count == 0) return;
        var discardCard = me.Hand.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, discardCard);

        // 成本已支付 → 记录每回合1次
        me.TurnOnceUsed.Add(key);

        // 效果 1：将对方最多 1 张费用≤7 的角色 KO
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 7).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤7 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 效果 2：对方丢弃其 1 张手牌
        if (ctx.Engine is not null)
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
    }
}
