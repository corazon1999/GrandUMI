using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-076 萨卡斯基（领航）
/// 【启动主要】【每回合1次】可以丢弃我方手牌中 1 张拥有《海军》特征的卡牌：
///   本回合中，对方最多 1 张角色费用 -1。
///
/// 实现说明：成本为"按特征(《海军》)过滤弃 1 张手牌"，DSL 的 handDiscard 不能按特征过滤，故脚本实现。
/// 费用 -1 用 ContinuousEffect.CostDelta（限本回合：Predicate 用 TurnCount 锁定当前回合）。
/// </summary>
public class P_076_Sakazuki : IScriptedEffect
{
    public string CardNumber => "P-076";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        var key = "P-076-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中拥有《海军》特征的卡牌
        var navy = me.Hand.Where(c => c.Info.HasKeyword("海军")).ToList();
        if (navy.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨卡斯基【启动主要】：丢弃 1 张《海军》手牌，使对方 1 张角色本回合费用 -1？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = navy.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardSel = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张《海军》手牌（成本）", navy.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (discardSel.Count == 0) return;
        var dcard = navy.First(c => c.Id.ToString() == discardSel[0]);
        AtomicOps.DiscardHand(me, dcard);
        me.TurnOnceUsed.Add(key);

        // 效果：对方最多 1 张角色本回合费用 -1
        var cands = opp.Characters;
        if (cands.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合费用 -1", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == pick[0]);
        var tgtId = tgt.Id;
        int turn = ctx.State.TurnCount;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = ctx.Source.Id.ToString() + ":costminus:" + tgtId,
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true, Filter = c => c.Id == tgtId },
            CostDelta = -1,
            Predicate = (s, sideIdx, c) => s.TurnCount == turn,
        });
    }
}
