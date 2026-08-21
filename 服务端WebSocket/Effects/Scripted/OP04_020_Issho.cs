using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-020 一生（领航 / 风・地 / 海军）
/// 【咚!!×1】【我方的回合中】对方所有角色费用-1。
/// 【我方的回合结束时】①（可以将费用区中指定数量的咚‼转为休息状态）：
///   将我方最多 1 张费用不高于 5 的角色转为活跃状态。
///
/// 实现说明：
///   - 持续费用修正：ContinuousEffect.CostDelta（领袖在 OnGameStart 注册）。
///     Side=1（对方）所有角色费用-1，条件：本卡被赋予咚!!≥1 且我方的回合中。
///   - 【我方的回合结束时】用 OnMyTurnEnd：支付横置 1 张活跃咚的成本后，
///     将我方最多 1 张费用≤5 的角色转为活跃。
/// </summary>
public class OP04_020_Issho : IScriptedEffect
{
    public string CardNumber => "OP04-020";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnGameStart || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            // 持续：【咚!!×1】【我方的回合中】对方所有角色费用-1
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
                CostDelta = -1,
                Predicate = (s, sideIdx, c) =>
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1,
            });
            return;
        }

        // 【我方的回合结束时】可选：将我方最多 1 张费用≤5 的角色转为活跃状态
        var cands = me.Characters
            .Where(c => c.IsTapped && ctx.State.CurrentCostOf(owner, c) <= 5)
            .ToList();
        var costDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active && d.AttachedToCardId is null);
        if (cands.Count == 0 || costDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "一生【我方的回合结束时】：将我方最多 1 张费用≤5 的角色转为活跃状态?");
        if (!use) return;

        costDon.State = DonState.Rest;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张费用不高于 5 的角色转为活跃状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (tgt is not null) AtomicOps.ActivateCard(tgt);
        }
    }
}
