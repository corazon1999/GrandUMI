using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-036 鬼气 九刀流 阿修罗 魔九闪（事件）
/// 【主要】本回合中，我方最多 1 张领袖或角色力量 +3000。之后，可以将我方 1 张费用为 3 或
///   更高的角色转为休息状态。那样做的场合，将对方最多 1 张费用不高于 5 的角色转为休息状态。
///
/// 说明 / 简化点：
///   - +3000 用 AddPowerThisTurn（本回合）。
///   - "可以横置我方≥3费角色"是可选成本，用 ConfirmOptional + ChooseCards；
///     只有实际横置了我方角色，才能横置对方≤5费角色（成本与结果强耦合，按文本顺序判定）。
/// </summary>
public class OP07_036_AshuraMakyusen : IScriptedEffect
{
    public string CardNumber => "OP07-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 本回合中，我方最多 1 张领袖或角色 +3000 ──
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var buffChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，选择我方最多 1 张领袖或角色力量 +3000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (buffChosen.Count > 0)
        {
            var t = buffTargets.First(c => c.Id.ToString() == buffChosen[0]);
            AtomicOps.AddPowerThisTurn(t, 3000);
        }

        // ── 可选成本：横置我方 1 张费用≥3 的角色 ──
        var costCands = me.Characters.Where(c => !c.IsTapped && ctx.State.CurrentCostOf(c) >= 3).ToList();
        if (costCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "是否将我方 1 张费用≥3 的角色转为休息状态？（那样做可横置对方 1 张费用≤5 的角色）");
        if (!use) return;

        var costChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张费用≥3 的角色转为休息状态",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costChosen.Count == 0) return;
        var paid = costCands.First(c => c.Id.ToString() == costChosen[0]);
        AtomicOps.RestCard(paid);

        // ── 结果：将对方最多 1 张费用≤5 的角色转为休息状态 ──
        var oppCands = opp.Characters.Where(c => !c.IsTapped && ctx.State.CurrentCostOf(c) <= 5).ToList();
        if (oppCands.Count == 0) return;
        var oppChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤5 的角色转为休息状态",
            oppCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (oppChosen.Count > 0)
        {
            var tgt = oppCands.First(c => c.Id.ToString() == oppChosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
